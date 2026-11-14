"""RSA PKCS#1 v1.5 signatures over SHA-256, in plain Python (no packages).

The same scheme the game verifies with .NET's RSA (ModelPack.Verify). Keys are dicts of ints:
{"n", "e", "d"}; only n and e (the public half) ever go into the build.
"""
import base64
import hashlib
import secrets

# DER prefix of a SHA-256 DigestInfo (RFC 8017 §9.2).
_SHA256_PREFIX = bytes.fromhex("3031300d060960864801650304020105000420")


def _probably_prime(n: int, rounds: int = 40) -> bool:
    if n < 4:
        return n in (2, 3)
    for p in (2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37):
        if n % p == 0:
            return n == p
    d, r = n - 1, 0
    while d % 2 == 0:
        d //= 2
        r += 1
    for _ in range(rounds):
        a = secrets.randbelow(n - 3) + 2
        x = pow(a, d, n)
        if x in (1, n - 1):
            continue
        for _ in range(r - 1):
            x = pow(x, 2, n)
            if x == n - 1:
                break
        else:
            return False
    return True


def _prime(bits: int) -> int:
    while True:
        c = secrets.randbits(bits) | (1 << (bits - 1)) | 1
        if _probably_prime(c):
            return c


def generate(bits: int = 2048, e: int = 65537) -> dict:
    while True:
        p, q = _prime(bits // 2), _prime(bits // 2)
        if p == q:
            continue
        phi = (p - 1) * (q - 1)
        try:
            d = pow(e, -1, phi)
        except ValueError:
            continue
        n = p * q
        if n.bit_length() == bits:
            return {"n": n, "e": e, "d": d}


def _k(n: int) -> int:
    return (n.bit_length() + 7) // 8


def _encode(message: bytes, k: int) -> bytes:
    t = _SHA256_PREFIX + hashlib.sha256(message).digest()
    if k < len(t) + 11:
        raise ValueError("key too short")
    return b"\x00\x01" + b"\xff" * (k - len(t) - 3) + b"\x00" + t


def sign(message: bytes, key: dict) -> str:
    k = _k(key["n"])
    m = int.from_bytes(_encode(message, k), "big")
    return base64.b64encode(pow(m, key["d"], key["n"]).to_bytes(k, "big")).decode()


def verify(message: bytes, signature: str, key: dict) -> bool:
    k = _k(key["n"])
    try:
        s = int.from_bytes(base64.b64decode(signature), "big")
    except ValueError:
        return False
    if s >= key["n"]:
        return False
    return pow(s, key["e"], key["n"]).to_bytes(k, "big") == _encode(message, k)


def b64int(x: int) -> str:
    return base64.b64encode(x.to_bytes((x.bit_length() + 7) // 8, "big")).decode()
