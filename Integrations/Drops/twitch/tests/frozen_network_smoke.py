"""Frozen Worker route probes with an isolated, local CONNECT fault relay."""
import argparse
import json
import select
import socket
import socketserver
import threading
import time
from pathlib import Path
from frozen_config_smoke import Client

class Relay(socketserver.ThreadingTCPServer):
    allow_reuse_address=True
    daemon_threads=True
    def __init__(self,mode):
        self.mode,self.calls,self.gate=mode,0,threading.Lock()
        super().__init__(('127.0.0.1',0),Handler)
        self.thread=threading.Thread(target=self.serve_forever,daemon=True)
        self.thread.start()
    def close(self):
        self.shutdown();self.server_close();self.thread.join(timeout=3)

class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        try:
            self.request.settimeout(30)
            header=b''
            while b'\r\n\r\n' not in header and len(header)<16384:
                chunk=self.request.recv(4096)
                if not chunk:return
                header+=chunk
            with self.server.gate:
                self.server.calls+=1
                number=self.server.calls
            if number==1:
                if self.server.mode=='timeout':time.sleep(16)
                if self.server.mode in {'fail','timeout','auth'}:
                    status=b'407 Proxy Authentication Required' if self.server.mode=='auth' else b'502 Bad Gateway'
                    self.request.sendall(b'HTTP/1.1 '+status+b'\r\nContent-Length: 0\r\nConnection: close\r\n\r\n')
                    return
            # No TLS termination: pass the encrypted tunnel to the existing proxy.
            with socket.create_connection(('127.0.0.1',7897),timeout=15) as upstream:
                upstream.sendall(header)
                while True:
                    ready,_,_=select.select([self.request,upstream],[],[],25)
                    if not ready:return
                    for source in ready:
                        data=source.recv(65536)
                        if not data:return
                        (upstream if source is self.request else self.request).sendall(data)
        except OSError:pass

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--worker',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=False)
    results=[]
    for name in ('proxy','direct','bad-fallback','bad-no-fallback','relay-recovery','relay-timeout','relay-407','restart'):
        root=args.output/name
        root.mkdir()
        (root/'settings.json').write_text('{"auto_claim_drops":false}',encoding='utf-8')
        relay=Relay({'relay-recovery':'fail','relay-timeout':'timeout','relay-407':'auth'}[name]) if name.startswith('relay-') else None
        proxy='http://127.0.0.1:'+str(relay.server_address[1] if relay else 9 if name.startswith('bad-') else 7897)
        policy={'enableProxy':name!='direct','proxyUrl':proxy,'fallbackDirect':name not in {'direct','bad-no-fallback'}}
        client=Client(args.worker.resolve(),root)
        try:
            client.request('hello')
            client.request('set_proxy',policy)
            result=client.request('network_probe',{'endpoint':'validate'},timeout=120)
            records=result['requests']
            routes=[r['route'] for r in records]
            if name in {'proxy','restart'}:
                assert result['endpoints'][0]['http']==401 and routes==['Proxy']
            elif name in {'relay-recovery','relay-timeout'}:
                assert routes in (['Proxy','DirectFallback','Proxy'],['Proxy','DirectFallback']) and records[-1]['http']==401
                recovered=client.request('network_probe',{'endpoint':'validate'},timeout=50)
                assert recovered['endpoints'][0]['http']==401 and recovered['requests'][-1]['route']=='Proxy'
            elif name=='relay-407':
                assert len(records)==1 and records[0]['http']==407
                recovered=client.request('network_probe',{'endpoint':'validate'},timeout=50)
                assert recovered['endpoints'][0]['http']==401 and recovered['requests'][-1]['route']=='Proxy'
            else:
                assert 1<=len(records)<=3
                if name=='direct': assert all(route=='Direct' for route in routes)
                elif name=='bad-fallback': assert routes in (['Proxy','DirectFallback','Proxy'],['Proxy','DirectFallback'])
                else: assert routes==['Proxy']*3 and not result['endpoints'][0]['reached']
            assert not result['authenticated'] # tokenless 401 is reachability, never login.
            client.request('stop')
            assert not client.request('load_state')['running']
        finally:
            client.close()
            if relay:relay.close()
        if name=='restart':
            replacement=Client(args.worker.resolve(),root)
            try:
                replacement.request('hello'); replacement.request('set_proxy',policy)
                assert replacement.request('network_probe',{'endpoint':'validate'},timeout=50)['endpoints'][0]['http']==401
            finally:replacement.close()
        results.append({'case':name,'status':'PASS','routes':routes,'requests':records,'exit':client.process.returncode})
        (args.output/'summary.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'PASS {len(results)} actual frozen Twitch network scenarios')

if __name__=='__main__':main()
