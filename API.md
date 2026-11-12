# 数据查询类 API

所有API的都是get接口，嗯……这很奇怪但先这样用着。

形式都是`域名/方法名?参数`，如果没有参数就是`域名/方法名`。

比如这样:

```txt
http://localhost:18765/colonists
http://localhost:18765/colonists?detail=true
```

下面来列举1下方法:

**things**
- 获取地图上所有物体的信息。
- 输出: `list`。

**zones**
- 获取当前地图所有的区域列表。
- 输出: `list`。

**rooms**
- 获取当前地图所有的房间列表。
- 输出: `list`。

**prisoners**
- 获取殖民地所有囚犯的信息。
- 输出: `list`。

**mechanoids**
- 获取殖民地所有机械体信息<sub>(不含敌对的)</sub>。
- 输出: `list`。

**colony**
- 获取殖民地当前的概况。
- 输出: `dict`。

**letters**
- 获取通知列表<sub>(就是屏幕右侧的信封)</sub>。
- 输出: `list`。

**alerts**
- 获取当前警报<sub>(就是屏幕右侧的「衣衫褴褛」之类的)</sub>
- 输出: `list`。

**colonists**
- 获取殖民地所有小人的信息。
- 输入: `bool detail`
    - detail: 默认是false，为true时会返回更详细的信息。
- 输出: `list`。

**map_tiles**
- 获取当前地图所有的格子信息。
- 输出: `list`。

**animals**
- 获取地图上的所有动物信息。
- 输出: `list`。

**storage_detail**
- 获取所有储存区和储物建筑中的详细物品清单。
- 输出: `list`。

**storage**
- 获取所有储存区和储物建筑的汇总物品清单。
- 输出: `list`。

**mods**
- 获取当前游戏正在运行的 Mod 列表。
- 输出: `list`。

**factions**
- 获取游戏内存在的所有势力信息。
- 输出: `list`。

**research**
- 获取当前的科研进度。
- 输出: `dict`。

**buildings**
- 获取地图上的所有建筑。
- 输出: `list`。

**map**
- 获取当前地图的全局环境数据。
- 输出: `dict`。

**biosculpter_pods**
- 获取地图上所有塑形舱的状态。
- 输出: `list`。

**get_production_recipe**
- 获取指定工作台当前的所有生产清单。
- 输入: `int id` 
    - id: 工作台的ID。
- 输出: `list`。


# 特殊 API

**thing_def**
- 获取游戏中的所有物品名称。
- 输出: 数组。

**recipe_def**
- 获取游戏中的所有配方名称。
- 输出: 数组。

**thing_category_def**
- 获取游戏中的所有物品分类。
- 输出: 数组。

**job_def**
- 获取游戏中的工作名称。
- 输出: 数组。


# 操作类 API

**input_blueprint**
- 放1个建筑蓝图。
- 输入: `int x, int y, string thing, string stuff, string rotation`
    - x和y: 目标地点的坐标。
    - thing: 建筑的 ThingDef 名称。
    - stuff: 建造所用材料的 ThingDef 名称。
    - rotation: 建筑的旋转方向，可选择`North, East, South, West`。
    - 样例: `input_blueprint(x=100, y=100, thing='Wall', stuff='WoodLog', rotation='North')`
- 备注: 
    - 如果不知道建筑或者材料的名称的话，可以用上面的`thing_def`接口查。

**input_surgery**
- 给小人添加手术清单。
- 输入: `int id, string recipe, string body_part`
    - id: 小人的ID。
    - recipe: 手术配方的 RecipeDef 名称。
    - body_part: 身体部位。
    - 样例: `input_surgery(id=12345, recipe='InstallPegLeg', body_part='左腿')`
- 备注: 
    - 如果不知道身体部位怎么写，可以随便写1个，抛出的异常里会提示小人所有可用的身体部位列表。
    - 不知道手术名称的话，可以用`recipe_def`接口查。
    - 这个body_part的名字是取决于本地化的，这很奇怪但我也没找到更好的实现方式，所以先这样吧。

**input_create_zone**
- 在指定坐标创建1个新的储存区。
- 输入: `string label, int x, int y`
    - label: 储存区的名称。
    - x和y: 起始单元格坐标。
    - 样例: `input_create_zone(label='生鲜存储', x=100, y=100)`
- 输出: 那个储存区的ID。

**input_zone_add_cell**
- 为已存在的区域添加1个单元格。
- 输入: `int zone_id, int x, int y`
    - zone_id: 区域的ID。
    - x和y: 要添加的单元格坐标。
    - 样例: `input_zone_add_cell(zone_id=1, x=101, y=100)`

**input_stockpile_zone_disallow_all**
- 清楚指定储存区的所有允许物品。
- 输入: `int zone_id`
    - zone_id: 储存区的 ID。
    - 样例: `input_stockpile_zone_disallow_all(zone_id=1)`

**input_stockpile_zone_allow**
- 允许指定储存区存放某1特定分类的物品。
- 输入: `int zone_id, string category`
    - zone_id: 储存区的 ID。
    - category: 物品分类的 ThingCategoryDef 名称。
    - 样例: `input_stockpile_zone_allow(zone_id=1, category='Foods')`
- 备注: 
    - 如果不知道category怎么写，可以用上面的`thing_category_def`接口查。

**input_stockpile_zone_priority**
- 设置指定储存区的优先级。
- 输入: `int zone_id, int priority`
    - zone_id: 储存区的 ID。
    - priority: 优先级枚举值(0到5) 。
    - 样例: `input_stockpile_zone_priority(zone_id=1, priority=4)`

**input_pawn_interact**
- 命令小人对指定目标执行特定工作。
- 输入: `int pawn_id, int target_id, string job`
    - pawn_id: 执行任务的ID。
    - target_id: 目标的 ID。
    - job: 工作的 JobDef 名称。
    - 样例: `input_pawn_interact(pawn_id=123, target_id=456, job='Deconstruct')`
- 备注:
    - 如果不知道category怎么写，可以用上面的`job_def`接口查。

**input_pawn_interact_biosculpter**
- 命令小人进入指定的塑形舱。
- 输入: `int pawn_id, int target_id, string cycle_key`
    - pawn_id: 小人ID。
    - target_id: 塑形舱的ID。
    - cycle_key: 循环类型。可选值为: `medic, bioregeneration, ageReversal, pleasure`。
    - 样例: `input_pawn_interact_biosculpter(pawn_id=123, target_id=456, cycle_key='medic')`

**input_message**
- 发送1条提示信息<sub>(是屏幕左上角的那个)</sub>
- 输入: `string text`
    - text: 要显示的消息内容。
    - 样例: `input_message(text='自动化脚本已连接')`

**input_command**
- 让小人放技能。
- 输入: `int id, string label, int target_id`
    - id: 小人ID。
    - label: 命令的名称。
    - target_id: 目标的ID<sub>(如果不取对象就传0)</sub>。
    - 样例: `input_command(id=123, label='转化钢铁', target_id=9)`
- 备注
    - label是取决于本地化的，还是很奇怪但我也没找到更好的实现方式，所以先这样吧。
    - 如果不知道label怎么填，可以用`colonists(detail=true)`，看那个人身上的`commands`。
        - 远程射击命令没有Label，这种情况下用commands的type代替label<sub>(就是填字符串'Verse.Command_VerbTarget`)</sub>。

**input_set_forbidden**
- 允许或禁用指定的物品。
- 输入: `int id, bool value`
    - id: 物品 ID。
    - value: 禁用或者允许<sub>(注意实际含义是相反的)</sub>。
    - 样例: `input_set_forbidden(id=456, value=true)`

**input_add_designation**
- 为指定物体添加计划<sub>(就是砍树的那个标记)</sub>。
- 输入: `int id, string type`
    - id: 目标对象的ID。
    - type: 计划的名字。
    - 样例: `input_add_designation(id=456, type='Designator_Mine')`
- 备注:
    - 如果不知道计划的名字，可以随便填1个，抛出的异常里会有所有的名字。

**input_remove_all_designation**
- 去除指定物体上的所有计划。
- 输入: `int id`
    - id: 目标物品 ID。
    - 样例: `input_remove_all_designation(id=456)`

**input_production_recipe**
- 为工作台添加生产清单。
- 输入: `int id, string recipe, string repeat_mode, int count`
    - id: 工作台建筑的 ID。
    - recipe: 配方的 RecipeDef 名称。
    - repeat_mode: 循环模式的，可选择`'RepeatCount', 'TargetCount', 'Forever'`。
    - count: 目标数量或重复次数。
    - 样例: `input_production_recipe(id=789, recipe='Make_Kibble', repeat_mode='TargetCount', count=50)`
