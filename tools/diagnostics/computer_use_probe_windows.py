"""Windows boundaries used only by the optional owned-window integration probe."""
import ctypes as c
from ctypes import wintypes as w
import time
import threading

k = c.WinDLL('kernel32', use_last_error=True)
a = c.WinDLL('advapi32', use_last_error=True)
k.CloseHandle.argtypes = [w.HANDLE]
k.LocalFree.argtypes = [c.c_void_p]
k.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]
k.CreateFileW.restype = w.HANDLE
a.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [w.LPCWSTR, w.DWORD, c.POINTER(c.c_void_p), c.c_void_p]
a.GetSecurityDescriptorDacl.argtypes = [c.c_void_p, c.POINTER(w.BOOL), c.POINTER(c.c_void_p), c.POINTER(w.BOOL)]
a.SetSecurityInfo.argtypes = [w.HANDLE, c.c_int, w.DWORD, c.c_void_p, c.c_void_p, c.c_void_p, c.c_void_p]
a.SetSecurityInfo.restype = w.DWORD
k.CreateMutexW.argtypes = [c.c_void_p, w.BOOL, w.LPCWSTR]
k.CreateMutexW.restype = w.HANDLE
k.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
k.ReleaseMutex.argtypes = [w.HANDLE]
k.CreateJobObjectW.argtypes = [c.c_void_p, w.LPCWSTR]
k.CreateJobObjectW.restype = w.HANDLE
k.SetInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD]
k.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
k.TerminateJobObject.argtypes = [w.HANDLE, w.UINT]
k.QueryInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD, c.c_void_p]


class BasicLimits(c.Structure):
    _fields_ = [('process_time', c.c_longlong), ('job_time', c.c_longlong), ('flags', w.DWORD),
                ('min_working_set', c.c_size_t), ('max_working_set', c.c_size_t), ('active_limit', w.DWORD),
                ('affinity', c.c_size_t), ('priority', w.DWORD), ('scheduling', w.DWORD)]


class ExtendedLimits(c.Structure):
    _fields_ = [('basic', BasicLimits), ('io', c.c_ulonglong * 6), ('process_memory', c.c_size_t),
                ('job_memory', c.c_size_t), ('peak_process', c.c_size_t), ('peak_job', c.c_size_t)]


class Accounting(c.Structure):
    _fields_ = [('times', c.c_longlong * 4), ('page_faults', w.DWORD), ('total', w.DWORD),
                ('active', w.DWORD), ('terminated', w.DWORD)]


def checked(value):
    if not value:
        raise c.WinError(c.get_last_error())
    return value


def restrict_pipe(path, sid):
    if not path.startswith('\\\\.\\pipe\\mycodex-cua-'):
        raise ValueError('Probe refused non-owned pipe')
    descriptor, dacl = c.c_void_p(), c.c_void_p()
    handle = k.CreateFileW(path, 0x60000, 3, None, 3, 0, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    try:
        checked(a.ConvertStringSecurityDescriptorToSecurityDescriptorW(
            'D:P(A;;GA;;;SY)(A;;GA;;;' + sid + ')', 1, c.byref(descriptor), None))
        checked(a.GetSecurityDescriptorDacl(descriptor, c.byref(w.BOOL()), c.byref(dacl), c.byref(w.BOOL())))
        failure = a.SetSecurityInfo(handle, 6, 4, None, None, dacl, None)
        if failure:
            raise c.WinError(failure)
    finally:
        if descriptor:
            k.LocalFree(descriptor)
        k.CloseHandle(handle)


class DesktopLease:
    # Acquire/release must run on the same RPC reader thread.
    def __init__(self, sid, session):
        self.handle = checked(k.CreateMutexW(None, False, 'Local\\MyCodex.ComputerUse.' + sid + '.' + session))
        self.owner = None

    def acquire(self, owner):
        if self.owner is not None:
            return False
        result = k.WaitForSingleObject(self.handle, 0)
        if result == 0x80:
            checked(k.ReleaseMutex(self.handle))
            return False
        if result == 0:
            self.owner = owner
            return True
        if result != 0x102:
            raise c.WinError(c.get_last_error())
        return False

    def release(self, owner):
        if owner == self.owner and self.owner is not None:
            checked(k.ReleaseMutex(self.handle))
            self.owner = None

    def close(self):
        k.CloseHandle(self.handle)


class Job:
    def __init__(self):
        self.lock = threading.Lock()
        self.closed = False
        self.handle = checked(k.CreateJobObjectW(None, None))
        limits = ExtendedLimits()
        limits.basic.flags = 0x2000  # KILL_ON_JOB_CLOSE
        checked(k.SetInformationJobObject(self.handle, 9, c.byref(limits), c.sizeof(limits)))

    def add(self, process):
        checked(k.AssignProcessToJobObject(self.handle, int(process._handle)))

    def active(self):
        accounting = Accounting()
        checked(k.QueryInformationJobObject(self.handle, 1, c.byref(accounting), c.sizeof(accounting), None))
        return accounting.active

    def close(self):
        with self.lock:
            if self.closed:
                return
            checked(k.TerminateJobObject(self.handle, 1))
            deadline = time.monotonic() + 5
            while self.active() and time.monotonic() < deadline:
                time.sleep(.02)
            empty = self.active() == 0
            k.CloseHandle(self.handle)
            self.closed = True
            assert empty, 'Owned process tree did not terminate'
