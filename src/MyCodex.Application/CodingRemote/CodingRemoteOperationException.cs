using System;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteOperationException : InvalidOperationException
    {
        public CodingRemoteOperationException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
