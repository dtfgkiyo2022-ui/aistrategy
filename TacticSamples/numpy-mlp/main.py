import numpy as np


def on_start(setup):
    global weights
    with np.load('models/policy.npz', allow_pickle=False) as model:
        weights = {key: model[key].copy() for key in ('w1', 'b1', 'w2', 'b2')}
    print('numpy MLP ready')


def features(view):
    own = sum(army['count'] for army in view['ownArmies'])
    seen_enemy = len(view['visibleEnemies'])
    return np.array([own / 100.0, seen_enemy / 100.0,
                     min(view['tick'] / 1200.0, 1.0), 1.0], dtype=np.float64)


def on_tick(view):
    hidden = np.maximum(features(view) @ weights['w1'] + weights['b1'], 0)
    attack = int(np.argmax(hidden @ weights['w2'] + weights['b2'])) == 1
    target_core = 3 - view['factionId'] if attack else view['factionId']
    commands = [{
        'type': 'policy', 'kind': 'Focus' if attack else 'Defend',
        'target': {'kind': 'Army', 'id': army['id']},
        'goal': {'kind': 'Core', 'id': target_core},
        'priority': 100, 'allowedLossPermille': 1000 if attack else 300,
        'reservePermille': 0,
    } for army in view['ownArmies']]
    return {'commands': commands}
