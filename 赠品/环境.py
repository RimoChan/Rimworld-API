import winreg
from pathlib import Path


steam文件夹 = Path(winreg.QueryValueEx(winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"), "SteamPath")[0])
rimworld文件夹 = steam文件夹 / 'steamapps/common/RimWorld'
mod文件夹 = rimworld文件夹 / 'Mods'
repo_root = (Path(__file__)/'../..').resolve()
