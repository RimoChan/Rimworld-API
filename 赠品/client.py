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
