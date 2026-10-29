import { randomUUID } from 'node:crypto';
import { createBrowserAccessHost } from './browser-access-host.mjs';

// Both surfaces follow the same parent-owned thread and turn lifecycle. Website
// permissions remain in the unmodified browser service, outside this feature gate.
export function createBrowserUseControl({ native, emit }) {
    const slots = new Set();
    const bindings = new Map();
    const allocating = new Map();
    const pending = new Map();
    let stopping = false;
    const invalid = () => new Error('CUA_BROWSER_CONTEXT_INVALID');
    const access = createBrowserAccessHost({ authorize: (request, { signal }) => new Promise(resolve => {
        if (stopping || signal.aborted) { resolve(false); return; }
        const id = randomUUID();
        const finish = allowed => {
            pending.delete(id);
            signal.removeEventListener('abort', abort);
            resolve(allowed === true && !signal.aborted && !stopping);
        };
        const abort = () => finish(false);
        pending.set(id, finish);
        signal.addEventListener('abort', abort, { once: true });
        try { emit({ event: 'browserPrepare', id, executorId: request.executorId,
            thread: request.sessionId, turn: request.turnId }); }
        catch { finish(false); }
    }) });

    async function shutdown() {
        if (stopping) return;
        stopping = true;
        for (const finish of [...pending.values()]) finish(false);
        await access.shutdown();
        bindings.clear();
        allocating.clear();
        slots.clear();
    }

    return {
        shutdown,
        async control(request) {
            if (stopping) throw invalid();
            const binding = bindings.get(request.slot);
            if (request.command === 'browserPrepareResult') {
                const finish = pending.get(request.requestId);
                if (!finish) return null;
                finish(request.prepared === true);
                return null;
            }
            if (request.command === 'allocateBrowser') {
                if (!slots.has(request.slot) || binding || allocating.has(request.slot)) throw invalid();
                const token = {};
                allocating.set(request.slot, token);
                try {
                    const created = await access.allocate();
                    if (stopping || !slots.has(request.slot) || allocating.get(request.slot) !== token) {
                        await access.remove(created.executorId);
                        throw invalid();
                    }
                    bindings.set(request.slot, { ...created, generation: 0 });
                    return created;
                } finally {
                    if (allocating.get(request.slot) === token) allocating.delete(request.slot);
                }
            }
            if (request.command === 'publishBrowser') {
                if (!binding) throw invalid();
                access.publish(binding.executorId);
                return null;
            }
            if (request.command === 'removeBrowser') {
                allocating.delete(request.slot);
                if (binding) {
                    bindings.delete(request.slot);
                    await access.remove(binding.executorId);
                }
                return null;
            }
            // Revoke first on cancellation, even if native cleanup subsequently fails.
            if (binding && request.command === 'end' &&
                (binding.turn === request.turn || binding.pendingTurn === request.turn)) {
                binding.generation++;
                access.end(binding.executorId, binding.turn);
                binding.turn = binding.pendingTurn = null;
            }
            if (binding && request.command === 'remove') {
                bindings.delete(request.slot);
                await access.remove(binding.executorId);
            }
            if (request.command === 'remove') { allocating.delete(request.slot); slots.delete(request.slot); }
            let generation;
            if (binding && (request.command === 'bind' || request.command === 'begin')) {
                generation = ++binding.generation;
                if ((request.command === 'bind' && binding.thread !== request.thread) ||
                    (request.command === 'begin' && binding.turn !== request.turn)) {
                    access.end(binding.executorId, binding.turn);
                    binding.turn = null;
                }
                binding.pendingTurn = request.command === 'begin' ? request.turn : null;
            }
            if (request.command === 'shutdown') await shutdown();
            const result = await native.control(request);
            if (request.command === 'allocate') slots.add(result.slot);
            // An end/remove during native preparation must prevent late reactivation.
            if (binding && bindings.get(request.slot) === binding && binding.generation === generation && !stopping) {
                if (request.command === 'bind') { access.bind(binding.executorId, request.thread); binding.thread = request.thread; }
                if (request.command === 'begin') { access.begin(binding.executorId, request.turn); binding.turn = request.turn; }
                binding.pendingTurn = null;
            }
            if (request.command === 'remove') slots.delete(request.slot);
            return result;
        }
    };
}
