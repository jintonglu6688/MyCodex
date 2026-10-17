using System;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerReviewTarget
    {
        public AppServerReviewTargetKind Kind { get; set; }

        public string Branch { get; set; }

        public string Sha { get; set; }

        public string Title { get; set; }

        public string Instructions { get; set; }

        public static AppServerReviewTarget CreateUncommittedChanges()
        {
            return new AppServerReviewTarget
            {
                Kind = AppServerReviewTargetKind.UncommittedChanges
            };
        }

        public static AppServerReviewTarget CreateBaseBranch(string branch)
        {
            return new AppServerReviewTarget
            {
                Kind = AppServerReviewTargetKind.BaseBranch,
                Branch = branch
            };
        }

        public static AppServerReviewTarget CreateCommit(string sha, string title)
        {
            return new AppServerReviewTarget
            {
                Kind = AppServerReviewTargetKind.Commit,
                Sha = sha,
                Title = title
            };
        }

        public static AppServerReviewTarget CreateCustom(string instructions)
        {
            return new AppServerReviewTarget
            {
                Kind = AppServerReviewTargetKind.Custom,
                Instructions = instructions
            };
        }

        public void Validate()
        {
            switch (Kind)
            {
                case AppServerReviewTargetKind.UncommittedChanges:
                    return;
                case AppServerReviewTargetKind.BaseBranch:
                    if (string.IsNullOrWhiteSpace(Branch))
                    {
                        throw new InvalidOperationException(
                            SharedLocalization.Text("AppServerReviewTarget.Error.BaseBranchRequired", "Review base branch cannot be empty."));
                    }

                    return;
                case AppServerReviewTargetKind.Commit:
                    if (string.IsNullOrWhiteSpace(Sha))
                    {
                        throw new InvalidOperationException(
                            SharedLocalization.Text("AppServerReviewTarget.Error.CommitShaRequired", "Review commit hash cannot be empty."));
                    }

                    return;
                case AppServerReviewTargetKind.Custom:
                    if (string.IsNullOrWhiteSpace(Instructions))
                    {
                        throw new InvalidOperationException(
                            SharedLocalization.Text("AppServerReviewTarget.Error.CustomInstructionsRequired", "Custom review instructions cannot be empty."));
                    }

                    return;
                default:
                    throw new InvalidOperationException(
                        SharedLocalization.Text("AppServerReviewTarget.Error.UnsupportedTarget", "Unsupported review target."));
            }
        }
    }
}
