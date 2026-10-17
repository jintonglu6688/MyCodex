using System;
using System.Runtime.InteropServices;
using MyCodex.Shared.Helpers;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WindowsCommandLineArgumentEscaperTests
    {
        [TestCase("plain")]
        [TestCase("C:\\repo\\")]
        [TestCase("C:\\repo with space\\")]
        [TestCase("two\\\\slashes and space")]
        [TestCase("quote \" inside")]
        [TestCase("")]
        public void Quote_RoundTripsThroughCommandLineToArgvW(string argument)
        {
            string commandLine = "git " + WindowsCommandLineArgumentEscaper.Quote(argument) + " next";
            string[] parsed = Parse(commandLine);

            Assert.That(parsed, Has.Length.EqualTo(3));
            Assert.That(parsed[0], Is.EqualTo("git"));
            Assert.That(parsed[1], Is.EqualTo(argument));
            Assert.That(parsed[2], Is.EqualTo("next"));
        }

        [Test]
        public void Quote_NullProducesEmptyArgument()
        {
            Assert.That(WindowsCommandLineArgumentEscaper.Quote(null), Is.EqualTo("\"\""));
        }

        private static string[] Parse(string commandLine)
        {
            int argc;
            IntPtr argv = CommandLineToArgvW(commandLine, out argc);
            if (argv == IntPtr.Zero)
            {
                throw new InvalidOperationException("CommandLineToArgvW failed.");
            }

            try
            {
                string[] result = new string[argc];
                for (int i = 0; i < argc; i++)
                {
                    IntPtr pointer = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    result[i] = Marshal.PtrToStringUni(pointer);
                }

                return result;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
            out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
