
import os
import shutil
import re
import sys
import subprocess
import urllib.request


ELEVATOR_CSPROJ = "src\\Elevator\\Elevator.csproj"
ELEVATOR_OUTPUT_FILE = "src\\Elevator\\bin\\Release\\Publish\\Elevator.exe"

JASM_CSPROJ = "src\\GIMI-ModManager.WinUI\\GIMI-ModManager.WinUI.csproj"
JASM_OUTPUT = "src\\GIMI-ModManager.WinUI\\bin\\Release\Publish\\"



INNO_SETUP_COMPILER = os.environ.get("ISCC_PATH", r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
INSTALLER_DIR = "installer"
INSTALLER_SCRIPT = os.path.join(INSTALLER_DIR, "setup.iss")
VC_REDIST_URL = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
VC_REDIST_PATH = os.path.join(INSTALLER_DIR, "redist", "vc_redist.x64.exe")

CHINESE_ISL_URL = "https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/Unofficial/ChineseSimplified.isl"
CHINESE_ISL_PATH = os.path.join(INSTALLER_DIR, "ChineseSimplified.isl")

RELEASE_DIR = "output"
JASM_RELEASE_DIR = "output\\JASM"

SelfContained =  sys.argv[1] == "SelfContained" if len(sys.argv) > 1  else False
ExcludeElevator = "ExcludeElevator" in sys.argv

def checkSuccessfulExitCode(exitCode: int) -> None:
	if exitCode != 0:
		print("Exit code: " + str(exitCode))
		exit(exitCode)

def extractVersionNumber() -> str:
	with open(JASM_CSPROJ, "r", encoding="utf-8") as jasmCSPROJ:
		for line in jasmCSPROJ:
			line = line.strip()
			if line.startswith("<VersionPrefix>"):
				return re.findall("\d+\.\d+\.\d+", line)

print("PostBuild.py")
print("PWD: " + os.getcwd())
print("SelfContained: " + str(SelfContained))

versionNumber = extractVersionNumber()
if versionNumber is None or len(versionNumber) == 0:
	print("Failed to extract version number from " + JASM_CSPROJ)
	exit(1)
versionNumber = versionNumber[0]

if (ExcludeElevator == False):
	print("Building Elevator...")
	elevatorPublishCommand = "dotnet publish " + ELEVATOR_CSPROJ + " /p:PublishProfile=FolderProfile.pubxml -c Release"
	print(elevatorPublishCommand)
	checkSuccessfulExitCode(os.system(elevatorPublishCommand))
	print()
	print("Finished building Elevator")
else:
	print("Skipping Elevator")
	print()

print("Building JASM...")
jasmPublishCommand = "dotnet publish " + JASM_CSPROJ + (" /p:PublishProfile=FolderProfileSelfContained.pubxml" if SelfContained else " /p:PublishProfile=FolderProfile.pubxml") + " -c Release" 
print(jasmPublishCommand)
checkSuccessfulExitCode(os.system(jasmPublishCommand))
print()
print("Finished building JASM")

# Create release directory
os.makedirs(RELEASE_DIR, exist_ok=True)
os.makedirs(JASM_RELEASE_DIR, exist_ok=True)

if (ExcludeElevator == False):
	print("Copying Elevator to JASM...")
	checkSuccessfulExitCode(os.system("copy " + ELEVATOR_OUTPUT_FILE + " " + JASM_RELEASE_DIR))
	print()
	print("Finished copying Elevator to release directory")

print("Copying JASM to output...")
shutil.copytree(JASM_OUTPUT, JASM_RELEASE_DIR, dirs_exist_ok=True)
print()
print("Finished copying JASM to release directory")

print("Copying text files to RELEASE_DIR...")
shutil.copy("Build\\README.txt", RELEASE_DIR)
shutil.copy("CHANGELOG.md", RELEASE_DIR + "\\CHANGELOG.txt")

print("Finished copying text files to release directory")

print("Zipping release directory...")
print("7z a -t7z -xm4 JASM.7z " + RELEASE_DIR)
releaseArchiveName = "JASM_v" + versionNumber + ".7z"

checkSuccessfulExitCode(os.system(f"7z a -mx4 {releaseArchiveName} .\\{RELEASE_DIR}\\*"))
print()
print("Finished zipping release directory")

checkSuccessfulExitCode(os.system(f"7z h -scrcsha256 .\\{releaseArchiveName}"))

# Build Inno Setup installer
print()
print("Building Inno Setup installer...")

# Download vc_redist.x64.exe if not present
os.makedirs(os.path.join(INSTALLER_DIR, "redist"), exist_ok=True)
if not os.path.exists(VC_REDIST_PATH):
	print(f"Downloading VC++ Redistributable from {VC_REDIST_URL}...")
	urllib.request.urlretrieve(VC_REDIST_URL, VC_REDIST_PATH)
	print("Download complete.")
else:
	print("VC++ Redistributable already exists, skipping download.")

if not os.path.exists(CHINESE_ISL_PATH):
	print(f"Downloading ChineseSimplified.isl from {CHINESE_ISL_URL}...")
	urllib.request.urlretrieve(CHINESE_ISL_URL, CHINESE_ISL_PATH)
	print("Download complete.")
else:
	print("ChineseSimplified.isl already exists, skipping download.")

# Compile installer
if os.path.exists(INNO_SETUP_COMPILER):
	print(f'Running: {INNO_SETUP_COMPILER} /DMyAppVersion="{versionNumber}" "{INSTALLER_SCRIPT}"')
	result = subprocess.run([INNO_SETUP_COMPILER, f'/DMyAppVersion={versionNumber}', INSTALLER_SCRIPT], check=False)
	checkSuccessfulExitCode(result.returncode)
	print("Finished building Inno Setup installer")
else:
	print(f"WARNING: Inno Setup compiler not found at {INNO_SETUP_COMPILER}, skipping installer build.")

setupFileName = f"JASM_v{versionNumber}_Setup.exe"
setupFilePath = os.path.join(INSTALLER_DIR, "output", setupFileName)

# Copy Setup.exe to workspace root for easy upload
if os.path.exists(setupFilePath):
	shutil.copy(setupFilePath, f".\\{setupFileName}")
	print(f"Setup file copied to: {setupFileName}")
	checkSuccessfulExitCode(os.system(f"7z h -scrcsha256 .\\{setupFileName}"))
else:
	print(f"WARNING: Setup file not found at {setupFilePath}")
	setupFileName = ""

# Export filenames to GITHUB_ENV
env_file = os.getenv('GITHUB_ENV')
if env_file is None:
	exit(1)

with open(env_file, "a") as myfile:
	myfile.write(f"zipFile={releaseArchiveName}\n")
	myfile.write(f"setupFile={setupFileName}\n")

exit(0)



