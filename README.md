# RimworldAPI

大家在玩Rimworld的时候，经常会有需要手动操作的情况，比如要手动去砍树，或者去拆除飞船残骸，很麻烦！

但这类杂事又不是那么少，如果1事做1 mod显得太杂，而且费时费力。

好在聪明的莉沫酱写了Rimworld-API！它可以向外提供1组HTTP API，用来读取游戏内的信息，或者模拟玩家输入，这样就可以使用Python自动控制你的小人了！


## 使用方式

下载方式有2种:
- 在[steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3731156014)直接订阅。
- 从[GitHub Releases](https://github.com/RimoChan/Rimworld-API/releases/)下载`RimworldAPI.zip`，然后解压到本地的Mods文件夹。

挂上之后，只要进入游戏，就会自动在`18765`端口启动1个HTTP server，然后用请求打它就可以了。

可以看这个demo视频，里面所有的操作都是通过API做的:

<video src="https://github.com/user-attachments/assets/23cf45dd-988a-4189-98ef-3f771a8d8f46"></video>


## 接口

大体上分为2类，是查询用的接口和输入用的接口。

具体接口有哪些可以看[这个文档](API.md)。


## 1些典型的用法

列举1些我自己常用的逻辑，大家可以集思广益:

- 查询存储区内的木材数量，如果木材数量小于400，就给离殖民地中心的5棵树添加砍伐标记。

- 查询所有囚犯，如果囚犯有腿就给它安装木腿，如果已经是木腿就把木腿砍了。

- 查询所有小人，如果有转化钢铁技能，就去用钢铁宏伟祭坛刷钱。


## 开发

因为我不喜欢Visual Studio，所以是直接调用`dotnet build`编译的，编译脚本在`Source\RimworldAPI\编译.py`。

编译成功后，建议做1下集成测试，测试脚本在`赠品\test.py`。使用方法是进入RimWorld的开发者模式，然后开1局快速测试<sub>(就是点开直接出现3个人的那个按钮)</sub>，然后运行`test.py`，能通过就可以了。


## 结束

最早的代码是从<https://github.com/MasterPNJ/API-REST-RimwOrld-Mod> Fork来的，不过因为我是傲娇所以不会感谢她！
