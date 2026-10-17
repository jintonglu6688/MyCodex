namespace MyCodex.Application.DTOs
{
    public sealed class RuntimeFileSystemOperationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public static RuntimeFileSystemOperationResult Ok(string message)
        {
            return new RuntimeFileSystemOperationResult
            {
                Success = true,
                Message = message ?? string.Empty
            };
        }

        public static RuntimeFileSystemOperationResult Fail(string message)
        {
            return new RuntimeFileSystemOperationResult
            {
                Success = false,
                Message = message ?? string.Empty
            };
        }
    }
}
