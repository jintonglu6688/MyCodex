using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class PersistenceRecoveryRequiredException : IOException
    {
        internal PersistenceRecoveryRequiredException(
            string primaryPath,
            string backupPath,
            Exception primaryError,
            Exception backupError)
            : base(
                "Persisted data could not be recovered. Repair or move the affected files, then restart MyCodex.",
                BuildInnerException(primaryError, backupError))
        {
            PrimaryPath = primaryPath;
            BackupPath = backupPath;
        }

        public string PrimaryPath { get; private set; }

        public string BackupPath { get; private set; }

        private static Exception BuildInnerException(Exception primaryError, Exception backupError)
        {
            if (primaryError != null && backupError != null)
            {
                return new AggregateException(primaryError, backupError);
            }

            return backupError ?? primaryError;
        }
    }

    internal static class PersistenceFileRecovery
    {
        internal static T Load<T>(
            string primaryPath,
            string backupPath,
            Func<string, T> read,
            Func<T> createMissing,
            Action blockSave)
        {
            try
            {
                return read(primaryPath);
            }
            catch (FileNotFoundException)
            {
                return createMissing();
            }
            catch (DirectoryNotFoundException)
            {
                return createMissing();
            }
            catch (Exception primaryError) when (
                primaryError is SerializationException ||
                primaryError is XmlException)
            {
                var recoveryTempPath = primaryPath + ".recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(backupPath, recoveryTempPath, false);
                    var recovered = read(recoveryTempPath);
                    RestoreBackup(primaryPath, recoveryTempPath);
                    return recovered;
                }
                catch (Exception backupError) when (
                    backupError is SerializationException ||
                    backupError is XmlException ||
                    backupError is InvalidDataException ||
                    backupError is IOException ||
                    backupError is UnauthorizedAccessException)
                {
                    blockSave();
                    throw new PersistenceRecoveryRequiredException(
                        primaryPath,
                        backupPath,
                        primaryError,
                        backupError);
                }
                finally
                {
                    TryDelete(recoveryTempPath);
                }
            }
            catch (IOException)
            {
                blockSave();
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                blockSave();
                throw;
            }
        }

        private static void RestoreBackup(string primaryPath, string recoveryTempPath)
        {
            if (File.Exists(primaryPath))
            {
                File.Replace(recoveryTempPath, primaryPath, null, true);
            }
            else
            {
                File.Move(recoveryTempPath, primaryPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
