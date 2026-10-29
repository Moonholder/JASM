import os
import subprocess
import sys

JASM_OUTPUT = os.path.join("src", "GIMI-ModManager.WinUI", "bin", "Release", "Publish")
DLL_PATH = os.path.join(JASM_OUTPUT, "JASM - Just Another Skin Manager.dll")
VERIFY_PROJECT = os.path.join("src", "Tools", "VerifyPublish", "VerifyPublish.csproj")

VERIFY_ARGS = [
    "GIMI_ModManager.WinUI.Models.GameBanana.GbModDisplayItem|get_ThumbnailImage",
    "GIMI_ModManager.WinUI.Models.PasswordEntry|get_DisplayName",
    "GIMI_ModManager.WinUI.Models.PasswordEntry|get_Password",
    "GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM|get_DisplayName",
    "GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM|get_FolderName",
    "GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM|get_Author",
    "GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM|get_IsEnabled",
    "GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM|get_Description",
    "GIMI_ModManager.WinUI.ViewModels.ModPageViewModels.ModFileInfoVm|get_IsBusy",
    "GIMI_ModManager.WinUI.ViewModels.ModPageViewModels.ModFileInfoVm|get_ShowDownloadButton",
    "GIMI_ModManager.WinUI.ViewModels.SubVms.GameComboBoxEntryVM|get_GameName",
    "GIMI_ModManager.WinUI.ViewModels.SubVms.GameComboBoxEntryVM|get_GameIconPath",
]

def main():
    if not os.path.exists(DLL_PATH):
        print(f"Error: Target DLL not found at {DLL_PATH}")
        sys.exit(1)

    print("Verifying publish output for trimming reflection bindings...")
    cmd = ["dotnet", "run", "--project", VERIFY_PROJECT, "-c", "Release", "--", DLL_PATH, *VERIFY_ARGS]
    print(" ".join(cmd))
    res = subprocess.run(cmd)
    if res.returncode != 0:
        print(f"Verify trimming failed with exit code {res.returncode}")
        sys.exit(res.returncode)
    print("Trimming verification passed!")

if __name__ == "__main__":
    main()
