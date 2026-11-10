using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerModelProviderException : InvalidOperationException
    {
        public AppServerModelProviderException(string message, string errorType, string modelProvider,
            string operation = null) : base(message)
        {
            ErrorType = errorType;
            ModelProvider = modelProvider;
            Operation = operation;
        }

        public string ErrorType { get; }
        public string ModelProvider { get; }
        public string Operation { get; }
    }
}
