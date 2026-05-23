import time
import shutil
import subprocess
from pathlib import Path

mod文件夹 = Path(f'D:/Steam/steamapps/common/RimWorld/Mods')
重启游戏 = True

if 重启游戏:
    c = subprocess.run(['taskkill', '/F', '/IM', 'RimWorldWin64.exe'])
    if c.returncode == 0:
        time.sleep(1)
    else:
        assert c.returncode == 128

subprocess.run(['dotnet', 'build'], check=True)

if (mod文件夹 / 'RimworldAPI').exists():
    shutil.rmtree(mod文件夹 / 'RimworldAPI')
(mod文件夹 / 'RimworldAPI/1.6/Assemblies').mkdir(parents=True, exist_ok=True)
shutil.copytree('../../outbuildings/About', mod文件夹 / 'RimworldAPI/About')
shutil.copytree('../../outbuildings/Languages', mod文件夹 / 'RimworldAPI/Languages')
shutil.copy('../../outbuildings/Newtonsoft.Json.dll', mod文件夹 / 'RimworldAPI/1.6/Assemblies')
shutil.copy('./bin/Debug/net472/RimworldAPI.dll', mod文件夹 / 'RimworldAPI/1.6/Assemblies')

if 重启游戏:
    subprocess.run('start D:/Steam/steamapps/common/RimWorld/RimWorldWin64.exe', shell=True, check=True)
