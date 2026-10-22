"""Loopback-only SFTP fixture. Requires paramiko; never installs a system service."""
import json
import os
import socket
import sys
import threading
import time
import base64
import hashlib
import hmac
import struct
import textwrap
from pathlib import Path
import paramiko

ROOT = Path(sys.argv[1]).resolve()
ROOT.mkdir(parents=True, exist_ok=True)
(ROOT / "site").mkdir(exist_ok=True)
host_key = paramiko.RSAKey.generate(2048)
client_key = paramiko.RSAKey.generate(2048)
client_key.write_private_key_file(str(ROOT / "client-key.pem"), password="local-key-pass")
host_key.write_private_key_file(str(ROOT / "host-key.pem"))
# A PPK v2 test key, following PuTTY Appendix C (fixture only, not product crypto).
def ssh_string(data): return struct.pack(">I", len(data)) + data
numbers = client_key.key.private_numbers()
private_message = paramiko.Message()
for number in (numbers.d, numbers.p, numbers.q, numbers.iqmp): private_message.add_mpint(number)
plain = private_message.asbytes()
plain += b"\0" * ((-len(plain)) % 16)
phrase = b"local-key-pass"
aes_key = (hashlib.sha1(struct.pack(">I", 0) + phrase).digest() + hashlib.sha1(struct.pack(">I", 1) + phrase).digest())[:32]
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
encrypt = Cipher(algorithms.AES(aes_key), modes.CBC(b"\0" * 16)).encryptor()
encrypted = encrypt.update(plain) + encrypt.finalize()
public = client_key.asbytes()
mac_key = hashlib.sha1(b"putty-private-key-file-mac-key" + phrase).digest()
mac_data = b"".join(ssh_string(x) for x in (b"ssh-rsa", b"aes256-cbc", b"fixture", public, plain))
pub_lines = textwrap.wrap(base64.b64encode(public).decode(), 64)
private_lines = textwrap.wrap(base64.b64encode(encrypted).decode(), 64)
(ROOT / "client-key.ppk").write_text("\n".join(["PuTTY-User-Key-File-2: ssh-rsa", "Encryption: aes256-cbc", "Comment: fixture", "Public-Lines: " + str(len(pub_lines)), *pub_lines, "Private-Lines: " + str(len(private_lines)), *private_lines, "Private-MAC: " + hmac.new(mac_key, mac_data, hashlib.sha1).hexdigest(), ""]), encoding="ascii")

class Auth(paramiko.ServerInterface):
    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if username == "fixture" and password == "local-test-pass" else paramiko.AUTH_FAILED
    def check_auth_publickey(self, username, key):
        return paramiko.AUTH_SUCCESSFUL if username == "fixture" and key == client_key else paramiko.AUTH_FAILED
    def get_allowed_auths(self, username): return "password,publickey"
    def check_channel_request(self, kind, channel_id):
        return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

class Files(paramiko.SFTPServerInterface):
    def local(self, remote):
        p = (ROOT / remote.lstrip("/")).resolve()
        if p != ROOT and ROOT not in p.parents: raise PermissionError("outside fixture")
        return p
    def canonicalize(self, remote): return "/" + str(self.local(remote).relative_to(ROOT)).replace("\\", "/").strip(".")
    def stat(self, remote):
        try: return paramiko.SFTPAttributes.from_stat(self.local(remote).stat())
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    lstat = stat
    def list_folder(self, remote):
        try:
            result = []
            for p in self.local(remote).iterdir():
                info = paramiko.SFTPAttributes.from_stat(p.stat()); info.filename = p.name; result.append(info)
            return result
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def open(self, remote, flags, attr):
        try:
            fd = os.open(self.local(remote), flags | getattr(os, "O_BINARY", 0), 0o666)
            mode = "r+b" if flags & os.O_RDWR else "wb" if flags & os.O_WRONLY else "rb"
            f = os.fdopen(fd, mode)
            h = paramiko.SFTPHandle(flags)
            if not flags & os.O_WRONLY: h.readfile = f
            if flags & (os.O_WRONLY | os.O_RDWR): h.writefile = f
            return h
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def mkdir(self, remote, attr):
        try: self.local(remote).mkdir(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def remove(self, remote):
        try: self.local(remote).unlink(); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    def rename(self, old, new):
        try: os.replace(self.local(old), self.local(new)); return paramiko.SFTP_OK
        except OSError as e: return paramiko.SFTPServer.convert_errno(e.errno)
    posix_rename = rename
    def chattr(self, remote, attr): return paramiko.SFTP_OK

def serve(conn):
    transport = paramiko.Transport(conn)
    try:
        transport.add_server_key(host_key)
        transport.set_subsystem_handler("sftp", paramiko.SFTPServer, Files)
        transport.start_server(server=Auth())
        while transport.is_active(): time.sleep(0.05)
    except (EOFError, OSError, paramiko.SSHException): pass
    finally: transport.close()

with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0)); listener.listen(20)
    (ROOT / "ready.json").write_text(json.dumps({"port": listener.getsockname()[1], "privateKey": str(ROOT / "client-key.pem")}), encoding="utf-8")
    print("SFTP fixture listening on loopback port", listener.getsockname()[1], flush=True)
    listener.settimeout(0.5)
    while not (ROOT / "stop").exists():
        try: conn, _ = listener.accept()
        except socket.timeout: continue
        threading.Thread(target=serve, args=(conn,), daemon=True).start()
