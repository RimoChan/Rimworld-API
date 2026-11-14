import shutil
import subprocess

from 环境 import mod文件夹, repo_root


def 编译():
    subprocess.run(['dotnet', 'build'], check=True, cwd=repo_root / 'Source/RimworldAPI')
    if (mod文件夹 / 'RimworldAPI').exists():
        shutil.rmtree(mod文件夹 / 'RimworldAPI')
    (mod文件夹 / 'RimworldAPI/1.6/Assemblies').mkdir(parents=True, exist_ok=True)
    shutil.copytree(repo_root / 'outbuildings/About', mod文件夹 / 'RimworldAPI/About')
    shutil.copytree(repo_root / 'outbuildings/Languages', mod文件夹 / 'RimworldAPI/Languages')
    shutil.copy(repo_root / 'outbuildings/Newtonsoft.Json.dll', mod文件夹 / 'RimworldAPI/1.6/Assemblies')
    shutil.copy(repo_root / 'Source/RimworldAPI/bin/Debug/net472/RimworldAPI.dll', mod文件夹 / 'RimworldAPI/1.6/Assemblies')


if __name__ == '__main__':
    编译()
