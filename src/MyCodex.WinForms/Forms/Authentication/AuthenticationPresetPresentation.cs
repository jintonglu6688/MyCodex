using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class AuthenticationPresetPresentation
    {
        public static int Priority(AuthenticationProviderPreset preset)
        {
            switch (preset.Id)
            {
                case "chatgpt": return 0;
                case "custom": return 1;
                case "official_api": return 2;
                default: return 3;
            }
        }

        public static string DisplayName(AuthenticationProviderPreset preset)
        {
            if (preset.Id == "chatgpt")
                return UiLocalization.Text("AuthenticationPreset.ChatGpt", "ChatGPT account authorization");
            if (preset.Id == "custom")
                return UiLocalization.Text("AuthenticationProviderSelection.Custom", "Custom provider");
            // Match upstream names, never positional cc-switch IDs. New or renamed
            // presets retain their original names until a translation is added.
            switch (preset.Name)
            {
                case "Kimi Global": return UiLocalization.Text("AuthenticationPreset.KimiGlobal", "Kimi Global");
                case "Kimi For Coding": return UiLocalization.Text("AuthenticationPreset.KimiCoding", "Kimi For Coding");
                case "Kimi For Coding Global": return UiLocalization.Text("AuthenticationPreset.KimiCodingGlobal", "Kimi For Coding Global");
                case "Volcengine Doubao": return UiLocalization.Text("AuthenticationPreset.Volcengine", "Volcengine Doubao");
                case "SiliconFlow": return UiLocalization.Text("AuthenticationPreset.SiliconFlow", "SiliconFlow");
                case "SiliconFlow en": return UiLocalization.Text("AuthenticationPreset.SiliconFlowGlobal", "SiliconFlow en");
                case "Qiniu": return UiLocalization.Text("AuthenticationPreset.Qiniu", "Qiniu");
                case "Compshare": return UiLocalization.Text("AuthenticationPreset.Compshare", "Compshare");
                case "Compshare Coding Plan": return UiLocalization.Text("AuthenticationPreset.CompshareCoding", "Compshare Coding Plan");
                case "Zhipu GLM": return UiLocalization.Text("AuthenticationPreset.Zhipu", "Zhipu GLM");
                case "Zhipu GLM en": return UiLocalization.Text("AuthenticationPreset.ZhipuGlobal", "Zhipu GLM en");
                case "Baidu Qianfan": return UiLocalization.Text("AuthenticationPreset.Baidu", "Baidu Qianfan");
                case "Baidu Qianfan Coding Plan": return UiLocalization.Text("AuthenticationPreset.BaiduCoding", "Baidu Qianfan Coding Plan");
                case "Baidu Qianfan Token Plan": return UiLocalization.Text("AuthenticationPreset.BaiduToken", "Baidu Qianfan Token Plan");
                case "Tencent Hunyuan": return UiLocalization.Text("AuthenticationPreset.Hunyuan", "Tencent Hunyuan");
                case "Tencent Token Plan": return UiLocalization.Text("AuthenticationPreset.TencentToken", "Tencent Token Plan");
                case "Tencent Token Plan (Intl)": return UiLocalization.Text("AuthenticationPreset.TencentTokenGlobal", "Tencent Token Plan (Intl)");
                case "Tencent Token Plan Enterprise Pro": return UiLocalization.Text("AuthenticationPreset.TencentPro", "Tencent Token Plan Enterprise Pro");
                case "Tencent Token Plan Enterprise Pro (Intl)": return UiLocalization.Text("AuthenticationPreset.TencentProGlobal", "Tencent Token Plan Enterprise Pro (Intl)");
                case "Tencent Token Plan Enterprise Lite": return UiLocalization.Text("AuthenticationPreset.TencentLite", "Tencent Token Plan Enterprise Lite");
                case "Tencent Token Plan Enterprise Lite (Intl)": return UiLocalization.Text("AuthenticationPreset.TencentLiteGlobal", "Tencent Token Plan Enterprise Lite (Intl)");
                case "StepFun API": return UiLocalization.Text("AuthenticationPreset.StepApi", "StepFun API");
                case "StepFun API en": return UiLocalization.Text("AuthenticationPreset.StepApiGlobal", "StepFun API en");
                case "StepFun": return UiLocalization.Text("AuthenticationPreset.Step", "StepFun");
                case "StepFun en": return UiLocalization.Text("AuthenticationPreset.StepGlobal", "StepFun en");
                case "ModelScope": return UiLocalization.Text("AuthenticationPreset.ModelScope", "ModelScope");
                case "MiniMax en": return UiLocalization.Text("AuthenticationPreset.MiniMaxGlobal", "MiniMax en");
                case "Xiaomi MiMo": return UiLocalization.Text("AuthenticationPreset.Xiaomi", "Xiaomi MiMo");
                case "Xiaomi MiMo Token Plan (China)": return UiLocalization.Text("AuthenticationPreset.XiaomiToken", "Xiaomi MiMo Token Plan (China)");
                default: return preset.Name ?? string.Empty;
            }
        }
    }
}

