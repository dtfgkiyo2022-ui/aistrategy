"""ゲームの外で実行する見本の重み生成。外部依存はnumpyだけ。"""
from pathlib import Path
import numpy as np


def main():
    destination = Path(__file__).parent / 'models'
    destination.mkdir(exist_ok=True)
    # 4入力 → ReLU 4ユニット → 守る/攻めるの2スコア。
    # 学習済みではなく、兵力と時間の意味が見える手書きの見本。
    w1 = np.eye(4, dtype=np.float64)
    b1 = np.zeros(4, dtype=np.float64)
    w2 = np.array([[0., 0.8], [0.4, -0.4], [0., 1.2], [0.7, 0.]])
    b2 = np.zeros(2, dtype=np.float64)
    np.savez(destination / 'policy.npz', w1=w1, b1=b1, w2=w2, b2=b2)


if __name__ == '__main__':
    main()
