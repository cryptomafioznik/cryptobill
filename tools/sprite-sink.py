#!/usr/bin/env python3
"""Раздаёт toys/ и принимает POST /save?name=<file> с data:URL — экспорт спрайтов
из браузерной игры её же функциями (bikeSprites при DPR=4) прямо в Unity/Resources/Art.
Зачем: чанки через консоль по 200 КБ — час на один байк; один POST — все семь за секунду."""
import base64, os, sys, urllib.parse
from http.server import SimpleHTTPRequestHandler, HTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'unity', 'Assets', 'ChartRunner', 'Resources', 'Art')

class H(SimpleHTTPRequestHandler):
    def __init__(self, *a, **k): super().__init__(*a, directory=os.path.join(ROOT, 'toys'), **k)
    def do_POST(self):
        q = urllib.parse.urlparse(self.path)
        if q.path != '/save': self.send_error(404); return
        name = urllib.parse.parse_qs(q.query).get('name', [''])[0]
        if not name or '/' in name or not name.endswith('.png'): self.send_error(400); return
        body = self.rfile.read(int(self.headers.get('Content-Length', 0))).decode()
        if not body.startswith('data:image/png;base64,'): self.send_error(400); return
        os.makedirs(OUT, exist_ok=True)
        with open(os.path.join(OUT, name), 'wb') as f: f.write(base64.b64decode(body.split(',', 1)[1]))
        self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
    def log_message(self, *a): pass

HTTPServer(('127.0.0.1', int(sys.argv[1]) if len(sys.argv) > 1 else 8123), H).serve_forever()
