namespace MyCodex.Application.DTOs
{
    public sealed class TextFileOpenResult
    {
        public bool Handled { get; set; }

        public bool OpenedInternally { get; set; }

        public string ErrorMessage { get; set; }

        public static TextFileOpenResult Success(bool openedInternally)
        {
            return new TextFileOpenResult
            {
                Handled = true,
                OpenedInternally = openedInternally
            };
        }

        public static TextFileOpenResult Failure(string errorMessage)
        {
            return new TextFileOpenResult
            {
                Handled = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
