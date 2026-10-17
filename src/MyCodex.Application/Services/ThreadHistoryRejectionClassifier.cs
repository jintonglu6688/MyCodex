using System;

namespace MyCodex.Application.Services
{
    // A provider refused state another provider left in the thread history: reasoning,
    // item IDs, compaction summaries, or encrypted fragments it cannot read. The
    // wording comes from the upstream proxy's opaque state rectifier.
    public static class ThreadHistoryRejectionClassifier
    {
        public static bool IsRepairableRejection(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return Contains(message, "invalid_encrypted_content") ||
                   (Contains(message, "encrypted content") &&
                    (Contains(message, "could not be decrypted") || Contains(message, "could not be verified"))) ||
                   Contains(message, "Encrypted function output content could not be decrypted") ||
                   Contains(message, "Could not decrypt the provided encrypted_content") ||
                   Contains(message, "Could not decode the compaction blob") ||
                   (Contains(message, "was not issued to this caller") &&
                    (Contains(message, "encrypted_content") || Contains(message, "reasoning"))) ||
                   (Contains(message, "Invalid 'input[") && Contains(message, "Expected an ID that begins with")) ||
                   (Contains(message, "not found") && Contains(message, "Items are not persisted when")) ||
                   (Contains(message, "Invalid 'input[") && Contains(message, ".content': array too long") &&
                    Contains(message, "maximum length 0"));
        }

        private static bool Contains(string message, string value)
        {
            return message.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
