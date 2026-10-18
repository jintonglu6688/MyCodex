using System.Collections.Generic;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class OptionsForm
    {
        private OfficialCodexSettingsForm _officialCodexSettingsForm;

        public bool HasOfficialCodexSettings
        {
            get { return _officialCodexSettingsForm != null; }
        }

        public OfficialCodexSettingsForm OfficialCodexSettings
        {
            get { return _officialCodexSettingsForm; }
        }

        private void AddOfficialCodexSettingsPage(
            AppServerConfigSnapshot snapshot,
            IEnumerable<AppServerModelOption> availableModels,
            IEnumerable<AppServerPermissionProfileSummary> permissionProfiles)
        {
            if (snapshot == null)
            {
                AddPage(
                    "officialSettings",
                    UiLocalization.Text("OptionsForm.Category.OfficialSettings", "Official Settings"),
                    CreateUnavailablePage(
                        UiLocalization.Text("OptionsForm.CodexUnavailable.Title", "Codex official settings unavailable"),
                        UiLocalization.Text("OptionsForm.CodexUnavailable.Body", "Connect App Server before changing Codex official settings.")));
                return;
            }

            _officialCodexSettingsForm = new OfficialCodexSettingsForm(
                snapshot,
                availableModels,
                permissionProfiles,
                false);
            AddPage(
                "officialSettings",
                UiLocalization.Text("OptionsForm.Category.OfficialSettings", "Official Settings"),
                PrepareOfficialCodexSettingsForm(_officialCodexSettingsForm));
        }

        private void ApplyOfficialCodexSettingsSelection()
        {
            if (_officialCodexSettingsForm != null)
            {
                _officialCodexSettingsForm.ApplySelection();
            }
        }

        private static Control PrepareOfficialCodexSettingsForm(Form form)
        {
            if (form.Controls.Count == 0)
            {
                return new Panel { Dock = DockStyle.Fill, TabStop = false };
            }

            var content = form.Controls[0];
            form.Controls.Remove(content);
            content.Dock = DockStyle.Fill;
            return content;
        }
    }
}
