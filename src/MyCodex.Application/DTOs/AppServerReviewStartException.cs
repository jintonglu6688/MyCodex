using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerReviewStartException : Exception
    {
        public AppServerReviewStartException(AppServerReviewStartResult createdReview, Exception innerException)
            : base(innerException.Message, innerException)
        {
            CreatedReview = createdReview;
        }

        public AppServerReviewStartResult CreatedReview { get; }
    }
}
