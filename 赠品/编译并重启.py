import os
import subprocess
import sys
import time

from 环境 import rimworld文件夹
from 编译 import 编译


c = subprocess.run(['taskkill', '/F', '/IM', 'RimWorldWin64.exe'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
if c.returncode == 0:
    time.sleep(1)
else:
    assert c.returncode == 128

编译()

subprocess.Popen([rimworld文件夹 / 'RimWorldWin64.exe'] + sys.argv[1:], cwd=rimworld文件夹)
