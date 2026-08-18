namespace GIMI_ModManager.WinUI.Models
{
    public class PasswordEntry
    {
        public string Password { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public PasswordEntry()
        {
        }

        public PasswordEntry(string password, string displayName)
        {
            Password = password;
            DisplayName = displayName;
        }

        public override string ToString()
        {
            return Password;
        }
    }
}