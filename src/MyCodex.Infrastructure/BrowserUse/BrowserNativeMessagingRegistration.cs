using System;
using System.Collections;
using System.Linq;
using Microsoft.Win32;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal interface IBrowserNativeMessagingRegistration
    {
        BrowserNativeMessagingValue Read(BrowserExtensionBrowser browser);
        void Write(BrowserExtensionBrowser browser, BrowserNativeMessagingValue value);
    }

    internal sealed class BrowserNativeMessagingValue
    {
        internal bool Exists;
        internal object Data;
        internal RegistryValueKind Kind;

        internal bool Matches(BrowserNativeMessagingValue other)
        {
            if (other == null || Exists != other.Exists || Kind != other.Kind) return false;
            if (Data is IList left && other.Data is IList right) return left.Cast<object>().SequenceEqual(right.Cast<object>());
            return Equals(Data, other.Data);
        }
    }

    internal sealed class BrowserNativeMessagingRegistration : IBrowserNativeMessagingRegistration
    {
        internal static string KeyPath(BrowserExtensionBrowser browser)
        {
            if (browser != BrowserExtensionBrowser.Edge && browser != BrowserExtensionBrowser.Chrome) throw new ArgumentOutOfRangeException(nameof(browser));
            return @"Software\" + (browser == BrowserExtensionBrowser.Edge ? @"Microsoft\Edge" : @"Google\Chrome") + @"\NativeMessagingHosts\com.openai.codexextension";
        }

        public BrowserNativeMessagingValue Read(BrowserExtensionBrowser browser)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath(browser), false))
            {
                if (key == null || !key.GetValueNames().Contains("")) return new BrowserNativeMessagingValue();
                return new BrowserNativeMessagingValue
                {
                    Exists = true, Kind = key.GetValueKind(""),
                    Data = key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                };
            }
        }

        public void Write(BrowserExtensionBrowser browser, BrowserNativeMessagingValue value)
        {
            if (!value.Exists)
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath(browser), true)) key?.DeleteValue("", false);
                return;
            }
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath(browser))) key.SetValue("", value.Data, value.Kind);
        }
    }
}
