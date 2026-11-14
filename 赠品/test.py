from collections import Counter

from client import Client


c = Client()

assert c.ping()
assert c.things()
assert c.mods()
assert c.prisoners() == []
assert c.biosculpter_pods() == []
assert c.mechanoids() == []
assert len(c.map_tiles()) == 250 ** 2
assert c.animals()
assert c.storage() == []
assert c.research()['currentProject'] == ''
assert len(c.factions()) > 1
assert c.zones() == []
assert c.map()['size'] == {'x': 250, 'y': 250}
assert c.colony()['colonistCount'] == 3
assert len(c.colonists()) == 3
assert len(c.colonists(detail=True)) == 3
c.letters()
c.alerts()
assert c.thing_def()
assert c.recipe_def()


def 测试禁用和取消禁用():
    a = [i['id'] for i in c.things() if i['is_forbidden']]
    assert len(a) > 0
    t = a[0]
    c.input_set_forbidden(id=t, value=False)
    b = [i['id'] for i in c.things() if i['is_forbidden']]
    assert len(a) == len(b) + 1
    c.input_set_forbidden(id=t, value=True)
    aa = [i['id'] for i in c.things() if i['is_forbidden']]
    assert len(a) == len(aa)


def _找1块空地(范围: int):
    m = c.map()
    buildings = c.buildings()
    中心 = m['size']['x'] // 2, m['size']['y'] // 2
    候选 = [(i, j) for i in range(1, m['size']['x']) for j in range(1, m['size']['y'])]
    候选.sort(key=lambda a: abs(a[0]-中心[0])+abs(a[1]-中心[1]))
    for x, y in 候选:
        for b in buildings:
            if abs(b['position']['x']-x)+abs(b['position']['y']-y) < 范围:
                break
        else:
            return x, y


def 测试放建筑():
    x, y = _找1块空地(5)
    for d in [-1, 0, 1]:
        c.input_blueprint(x=x+d, y=y+d, thing='Wall', stuff='WoodLog', rotation='North')
    assert Counter(i['type'] for i in c.things())['RimWorld.Blueprint_Build'] == 3


def 测试征召():
    for p in c.colonists(detail=True):
        assert all(cmd['key'] for cmd in p['commands'])
        c.input_command(id=p['id'], key='Draft')
        p2 = c.colonist(id=p['id'])
        assert p2['isDrafted']
        keys = [cmd['key'] for cmd in p2['commands']]
        assert all(keys) and len(keys) == len(set(keys))
        c.input_command(id=p['id'], key='Draft')
        assert not c.colonist(id=p['id'])['isDrafted']


def 测试拿枪():
    gun = [t for t in c.things() if t['def'] == 'Gun_Revolver'][0]
    p = c.colonists()[0]
    c.input_pawn_interact(pawn_id=p['id'], target_id=gun['id'], job='Equip')
    assert c.colonist(id=p['id'])['currentJob'] == 'Equip'


def 测试种地():
    x, y = _找1块空地(5)
    zid = c.input_create_zone(label='菜地', x=x+1, y=y, type='Zone_Growing')
    c.input_zone_add_cell(zone_id=zid, x=x+2, y=y)
    c.input_growing_zone_set_allow(zone_id=zid, allow_sow=False, allow_harvest=False)
    z = [i for i in c.zones() if i['id'] == zid][0]
    assert z['label'] == '菜地' and z['type'] == 'Zone_Growing' and z['cellCount'] == 2
    assert not z['allowSow'] and not z['allowCut']


测试禁用和取消禁用()
测试放建筑()
测试征召()
测试拿枪()
测试种地()


print('通过！')
