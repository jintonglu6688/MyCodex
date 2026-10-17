namespace MyCodex.Application.InternalTools
{
    public sealed class InternalToolContentItem
    {
        public string Type { get; set; }

        public string Text { get; set; }

        public string ImageUrl { get; set; }

        public static InternalToolContentItem TextItem(string text)
        {
            return new InternalToolContentItem
            {
                Type = "inputText",
                Text = text ?? string.Empty
            };
        }

        public static InternalToolContentItem ImageItem(string imageUrl)
        {
            return new InternalToolContentItem
            {
                Type = "inputImage",
                ImageUrl = imageUrl ?? string.Empty
            };
        }
    }
}
