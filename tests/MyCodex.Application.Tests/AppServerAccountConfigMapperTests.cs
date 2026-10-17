using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerAccountConfigMapperTests
    {
        [Test]
        public void BuildAuthenticationState_MapsOfficialAccountReadState()
        {
            var accountState = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "requiresOpenaiAuth", true },
                {
                    "account",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "chatgpt" },
                        { "email", "user@example.com" },
                        { "planType", "team" }
                    }
                }
            };

            var result = AppServerAccountConfigMapper.BuildAuthenticationState(accountState);

            Assert.That(result.HasAccount, Is.True);
            Assert.That(result.IsAuthenticated, Is.True);
            Assert.That(result.RequiresOpenAiAuth, Is.True);
            Assert.That(result.AuthMode, Is.EqualTo("chatgpt"));
            Assert.That(result.AccountType, Is.EqualTo("chatgpt"));
            Assert.That(result.Email, Is.EqualTo("user@example.com"));
            Assert.That(result.PlanType, Is.EqualTo("team"));
        }

        [Test]
        public void BuildAuthenticationState_MapsNoAccountWithoutInventingAuthentication()
        {
            var accountState = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "requiresOpenaiAuth", true },
                { "account", null }
            };

            var result = AppServerAccountConfigMapper.BuildAuthenticationState(accountState);

            Assert.That(result.HasAccount, Is.False);
            Assert.That(result.IsAuthenticated, Is.False);
            Assert.That(result.RequiresOpenAiAuth, Is.True);
            Assert.That(result.AuthMode, Is.Null);
        }

        [Test]
        public void BuildAuthenticationState_UsesAccountUserEmailWhenTopLevelEmailIsMissing()
        {
            var accountState = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "account",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "chatgpt" },
                        {
                            "user",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "email", "fallback-user@example.com" }
                            }
                        },
                        { "planType", "team" }
                    }
                }
            };

            var result = AppServerAccountConfigMapper.BuildAuthenticationState(accountState);

            Assert.That(result.Email, Is.EqualTo("fallback-user@example.com"));
        }

        [Test]
        public void BuildAuthenticationState_DoesNotInventMissingAccountEmail()
        {
            var accountState = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "requiresOpenaiAuth", true },
                {
                    "account",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "chatgpt" },
                        { "planType", "team" }
                    }
                }
            };

            var result = AppServerAccountConfigMapper.BuildAuthenticationState(accountState);

            Assert.That(result.Email, Is.Null);
        }

        [Test]
        public void BuildAuthenticationState_MapsOfficialApiKeyAccountType()
        {
            var accountState = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "requiresOpenaiAuth", false },
                {
                    "account",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "apiKey" }
                    }
                }
            };

            var result = AppServerAccountConfigMapper.BuildAuthenticationState(accountState);

            Assert.That(result.IsAuthenticated, Is.True);
            Assert.That(result.AuthMode, Is.EqualTo("apikey"));
            Assert.That(result.AccountType, Is.EqualTo("apiKey"));
        }

        [Test]
        public void BuildRateLimitsSnapshot_MapsBucketsAndWindowDetails()
        {
            var resetsAt = 1710000000L;
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "rateLimitResetCredits",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "availableCount", 2 }
                    }
                },
                {
                    "rateLimitsByLimitId",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "model:gpt-5",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "limitName", "GPT-5" },
                                { "planType", "pro" },
                                {
                                    "primary",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "usedPercent", "12.5" },
                                        { "windowDurationMins", 300 },
                                        { "resetsAt", resetsAt }
                                    }
                                },
                                {
                                    "credits",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "hasCredits", true },
                                        { "unlimited", false },
                                        { "balance", "42" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(result);

            Assert.That(snapshot.Buckets, Has.Count.EqualTo(1));
            Assert.That(snapshot.RateLimitResetCreditsAvailableCount, Is.EqualTo(2));
            Assert.That(snapshot.Buckets[0].LimitId, Is.EqualTo("model:gpt-5"));
            Assert.That(snapshot.Buckets[0].LimitName, Is.EqualTo("GPT-5"));
            Assert.That(snapshot.Buckets[0].PlanType, Is.EqualTo("pro"));
            Assert.That(snapshot.Buckets[0].Primary.UsedPercent, Is.EqualTo(12.5m));
            Assert.That(snapshot.Buckets[0].Primary.WindowDurationMinutes, Is.EqualTo(300));
            Assert.That(snapshot.Buckets[0].Primary.ResetsAtLocalTime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(resetsAt).LocalDateTime));
            Assert.That(snapshot.Buckets[0].Credits.HasCredits, Is.True);
            Assert.That(snapshot.Buckets[0].Credits.Unlimited, Is.False);
            Assert.That(snapshot.Buckets[0].Credits.Balance, Is.EqualTo("42"));
        }

        [Test]
        public void BuildRateLimitsSnapshot_FallsBackToSingleRateLimitsPayload()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "rateLimits",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "limitId", "single" },
                        { "limitName", "Single bucket" }
                    }
                }
            };

            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(result);

            Assert.That(snapshot.Buckets, Has.Count.EqualTo(1));
            Assert.That(snapshot.Buckets[0].LimitId, Is.EqualTo("single"));
        }

        [Test]
        public void BuildRateLimitsSnapshot_LeavesResetCreditsNullWhenPayloadIsAbsent()
        {
            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(
                new Dictionary<string, object>(StringComparer.Ordinal));

            Assert.That(snapshot.ResetCredits, Is.Null);
        }

        [Test]
        public void BuildRateLimitsSnapshot_MapsAvailableCountWithoutInventingResetCreditDetails()
        {
            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "rateLimitResetCredits",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "availableCount", 3 }
                        }
                    }
                });

            Assert.That(snapshot.RateLimitResetCreditsAvailableCount, Is.EqualTo(3));
            Assert.That(snapshot.ResetCredits, Is.Null);
        }

        [Test]
        public void BuildRateLimitsSnapshot_MapsExplicitEmptyResetCreditDetails()
        {
            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "rateLimitResetCredits",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "availableCount", 0 },
                            { "credits", new object[0] }
                        }
                    }
                });

            Assert.That(snapshot.ResetCredits, Is.Not.Null);
            Assert.That(snapshot.ResetCredits, Is.Empty);
        }

        [Test]
        public void BuildRateLimitsSnapshot_MapsResetCreditRowsWithoutRecomputingAvailableCount()
        {
            var grantedAt = 1710000000L;
            var expiresAt = 1710003600L;
            var snapshot = AppServerAccountConfigMapper.BuildRateLimitsSnapshot(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "rateLimitResetCredits",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "availableCount", 5 },
                            {
                                "credits",
                                new object[]
                                {
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "id", "credit-1" },
                                        { "resetType", "weekly" },
                                        { "status", "available" },
                                        { "grantedAt", grantedAt },
                                        { "expiresAt", expiresAt },
                                        { "title", "Weekly reset" },
                                        { "description", "One extra reset" }
                                    },
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "id", "credit-2" },
                                        { "resetType", "monthly" },
                                        { "status", "redeemed" },
                                        { "grantedAt", grantedAt },
                                        { "title", "Monthly reset" },
                                        { "description", "Already used" }
                                    }
                                }
                            }
                        }
                    }
                });

            Assert.That(snapshot.RateLimitResetCreditsAvailableCount, Is.EqualTo(5));
            Assert.That(snapshot.ResetCredits, Has.Count.EqualTo(2));
            Assert.That(snapshot.ResetCredits[0].Id, Is.EqualTo("credit-1"));
            Assert.That(snapshot.ResetCredits[0].ResetType, Is.EqualTo("weekly"));
            Assert.That(snapshot.ResetCredits[0].Status, Is.EqualTo("available"));
            Assert.That(snapshot.ResetCredits[0].GrantedAtLocalTime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(grantedAt).LocalDateTime));
            Assert.That(snapshot.ResetCredits[0].ExpiresAtLocalTime, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(expiresAt).LocalDateTime));
            Assert.That(snapshot.ResetCredits[0].Title, Is.EqualTo("Weekly reset"));
            Assert.That(snapshot.ResetCredits[0].Description, Is.EqualTo("One extra reset"));
            Assert.That(snapshot.ResetCredits[1].ExpiresAtLocalTime, Is.Null);
        }

        [Test]
        public void BuildConfigSnapshot_MapsConfigOriginsAndLayers()
        {
            WithCulture("en-US", () =>
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "config",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "model", "gpt-5.4-mini" },
                            { "review_model", "review-model" },
                            { "cli_auth_credentials_store", "keyring" },
                            { "model_reasoning_effort", "high" },
                            {
                                "approval_policy",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    { "granular", true }
                                }
                            },
                            { "sandbox_mode", "workspace-write" },
                            { "model_context_window", "128000" },
                            { "tool_output_token_limit", 4096 },
                            {
                                "tools",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    { "view_image", true },
                                    {
                                        "web_search",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "context_size", "medium" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    {
                        "origins",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "model",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    { "version", "v1" },
                                    {
                                        "name",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "type", "user" },
                                            { "file", @"\\?\E:\Users\demo\.codex\config.toml" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    {
                        "layers",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "version", "v2" },
                                { "disabledReason", "none" },
                                {
                                    "name",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "type", "mdm" },
                                        { "domain", "corp" },
                                        { "key", "policy" }
                                    }
                                }
                            }
                        }
                    }
                };

                var snapshot = AppServerAccountConfigMapper.BuildConfigSnapshot(result);

                Assert.That(snapshot.ModelId, Is.EqualTo("gpt-5.4-mini"));
                Assert.That(snapshot.ReviewModelId, Is.EqualTo("review-model"));
                Assert.That(snapshot.AuthCredentialsStore, Is.EqualTo("keyring"));
                Assert.That(snapshot.ModelReasoningEffort, Is.EqualTo("high"));
                Assert.That(snapshot.ApprovalPolicy, Is.EqualTo("granular"));
                Assert.That(snapshot.SandboxMode, Is.EqualTo("workspace-write"));
                Assert.That(snapshot.ModelContextWindow, Is.EqualTo(128000L));
                Assert.That(snapshot.ToolOutputTokenLimit, Is.EqualTo(4096L));
                Assert.That(snapshot.ViewImageToolEnabled, Is.True);
                Assert.That(snapshot.WebSearchContextSize, Is.EqualTo("medium"));
                Assert.That(snapshot.Origins["model"].SourceType, Is.EqualTo("user"));
                Assert.That(snapshot.Origins["model"].SourceText, Is.EqualTo(@"User config: E:\Users\demo\.codex\config.toml"));
                Assert.That(snapshot.Layers, Has.Count.EqualTo(1));
                Assert.That(snapshot.Layers[0].SourceType, Is.EqualTo("mdm"));
                Assert.That(snapshot.Layers[0].SourceText, Is.EqualTo("MDM: corp/policy"));
                Assert.That(snapshot.Layers[0].Version, Is.EqualTo("v2"));
            });
        }

        [Test]
        public void BuildModelOption_UsesIdFallbackAndMapsReasoningEfforts()
        {
            var source = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "id", "gpt-5.4-mini" },
                { "description", "Fast model" },
                { "isDefault", true },
                { "defaultReasoningEffort", "medium" },
                {
                    "supportedReasoningEfforts",
                    new object[]
                    {
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "reasoningEffort", "low" },
                            { "description", "Fast" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "reasoningEffort", "max" },
                            { "description", "Maximum" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "reasoningEffort", "ultra" },
                            { "description", "Ultra" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "description", "Ignored because id is missing" }
                        }
                    }
                }
            };

            var option = AppServerAccountConfigMapper.BuildModelOption(source);

            Assert.That(option.ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(option.DisplayName, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(option.Description, Is.EqualTo("Fast model"));
            Assert.That(option.IsDefault, Is.True);
            Assert.That(option.DefaultReasoningEffort, Is.EqualTo("medium"));
            Assert.That(option.ReasoningEfforts, Has.Count.EqualTo(3));
            Assert.That(option.ReasoningEfforts[0].EffortId, Is.EqualTo("low"));
            Assert.That(option.ReasoningEfforts[0].Description, Is.EqualTo("Fast"));
            Assert.That(option.ReasoningEfforts[1].EffortId, Is.EqualTo("max"));
            Assert.That(option.ReasoningEfforts[2].EffortId, Is.EqualTo("ultra"));
        }

        [Test]
        public void BuildModelOption_MapsOfficialServiceTiersAndDefault()
        {
            var option = AppServerAccountConfigMapper.BuildModelOption(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", "gpt-5.6" },
                    { "defaultServiceTier", "fast" },
                    {
                        "serviceTiers",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "id", "fast" },
                                { "name", "Fast" },
                                { "description", "Low latency" }
                            },
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "id", "flex" },
                                { "name", "Flex" },
                                { "description", "Lower priority" }
                            }
                        }
                    },
                    {
                        "additionalSpeedTiers",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal) { { "id", "legacy" } }
                        }
                    }
                });

            Assert.That(option.DefaultServiceTier, Is.EqualTo("fast"));
            Assert.That(option.ServiceTiers, Has.Count.EqualTo(2));
            Assert.That(option.ServiceTiers[0].Id, Is.EqualTo("fast"));
            Assert.That(option.ServiceTiers[0].Name, Is.EqualTo("Fast"));
            Assert.That(option.ServiceTiers[0].Description, Is.EqualTo("Low latency"));
            Assert.That(option.ServiceTiers[1].Id, Is.EqualTo("flex"));
        }

        [Test]
        public void BuildModelOption_UsesLegacyServiceTiersOnlyWhenOfficialListIsEmpty()
        {
            var option = AppServerAccountConfigMapper.BuildModelOption(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", "gpt-5.6" },
                    { "serviceTiers", new object[0] },
                    {
                        "additionalSpeedTiers",
                        new object[]
                        {
                            "fast"
                        }
                    }
                });

            Assert.That(option.ServiceTiers, Has.Count.EqualTo(1));
            Assert.That(option.ServiceTiers[0].Id, Is.EqualTo("fast"));
            Assert.That(option.ServiceTiers[0].Name, Is.EqualTo("fast"));
            Assert.That(option.ServiceTiers[0].Description, Is.Null);
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        }
    }
}
