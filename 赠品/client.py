import time
import logging
from typing import Callable

import requests


class Client:
    def __init__(self, endpoint: str = 'http://127.0.0.1:18765'):
        self.endpoint = endpoint

    def __getattr__(self, k, timeout=3) -> Callable:
        def f(**kwargs):
            t = time.time()
            r = requests.post(f'{self.endpoint}/{k}', params=kwargs, timeout=timeout)
            r.reason = r.text
            r.raise_for_status()
            logging.info(f'[{k}]latency: {time.time()-t}')
            return r.json()
        return f

    def wait_for_start(self, timeout=60):
        n = timeout//2+1
        for i in range(n):
            try:
                self.ping()
            except requests.exceptions.ConnectionError:
                if i == n-1:
                    raise
                print('游戏还没起来，等1下')
                time.sleep(2)
            else:
                return
