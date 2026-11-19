using System.IO;
using TiltBox.Meta;
using UnityEngine;

namespace TiltBox.Game
{
    /// <summary>
    /// Save files in Application.persistentDataPath (internal app storage on Android, user profile on PC).
    /// Each write goes to a temp file first and then replaces the target, so a crash mid-write can't leave a
    /// half-written file; <see cref="SaveStore"/> adds the backup copy on top.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private readonly string _dir;

        public FileSaveStorage(string directory = null)
        {
            _dir = directory ?? Application.persistentDataPath;
            Directory.CreateDirectory(_dir);
        }

        public string Folder => _dir;

        public string Read(string name)
        {
            string path = Path.Combine(_dir, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public void Write(string name, string text)
        {
            string path = Path.Combine(_dir, name);
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }
}
