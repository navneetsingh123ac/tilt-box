using System;

namespace TiltBox.Meta
{
    /// <summary>Where save text lives. The game uses files in Application.persistentDataPath; tests use memory.</summary>
    public interface ISaveStorage
    {
        /// <summary>The stored text, or null if there is none.</summary>
        string Read(string name);

        void Write(string name, string text);
    }

    /// <summary>
    /// GDD §15: "Keep a backup copy and restore it if the main file is damaged." Every save writes the main file
    /// and then the backup, so a crash during either write leaves at least one good copy.
    /// </summary>
    public sealed class SaveStore
    {
        public const string MainName = "save.json";
        public const string BackupName = "save.backup.json";

        private readonly ISaveStorage _storage;

        public SaveStore(ISaveStorage storage) => _storage = storage ?? throw new ArgumentNullException(nameof(storage));

        /// <summary>How the last <see cref="Load"/> went, for logs and tests.</summary>
        public string LastLoadSource { get; private set; } = "";

        public SaveData Load()
        {
            SaveData data = TryRead(MainName);
            if (data != null) { LastLoadSource = "main"; return data; }
            data = TryRead(BackupName);
            if (data != null) { LastLoadSource = "backup"; return data; }
            LastLoadSource = "new";
            return new SaveData();
        }

        public void Save(SaveData data)
        {
            string text = SaveJson.Write(data);
            _storage.Write(MainName, text);
            _storage.Write(BackupName, text);
        }

        private SaveData TryRead(string name)
        {
            try
            {
                string text = _storage.Read(name);
                return string.IsNullOrEmpty(text) ? null : SaveJson.Read(text);
            }
            catch (FormatException)
            {
                return null; // damaged: try the next copy
            }
        }
    }
}
