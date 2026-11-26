import base64
import hashlib
import os

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# Task 4, la fel ca in Crypto.cs: cheie din parola (PBKDF2) + criptare AES-GCM.
# Toti parametrii trebuie sa fie identici cu cei din C#, altfel cheile difera
# si mesajele private nu se mai pot decripta intre cele doua limbaje.
SALT = b"pr-lab3-lan-chat"   # fix: toti ajung la aceeasi cheie fara sa-si trimita nimic
ITERATIONS = 200_000         # lent intentionat, ca sa fie greu de ghicit parola prin incercari
KEY_SIZE = 32                # AES-256
NONCE_SIZE = 12
TAG_SIZE = 16


class Crypto:
    def __init__(self, passphrase):
        key = hashlib.pbkdf2_hmac("sha256", passphrase.encode("utf-8"), SALT, ITERATIONS, KEY_SIZE)
        self._aes = AESGCM(key)

    # Intoarce nonce-ul si (ciphertext + tag), ambele in base64 ca sa intre in JSON.
    def encrypt(self, text, author):
        # nonce random, nou la fiecare mesaj. acelasi nonce de 2 ori cu aceeasi cheie = GCM e spart
        nonce = os.urandom(NONCE_SIZE)
        # autorul e "associated data": nu e criptat, dar daca il schimba cineva, tag-ul nu mai bate.
        # AESGCM.encrypt intoarce deja ciphertext + tag lipite, exact formatul din C#.
        data = self._aes.encrypt(nonce, text.encode("utf-8"), author.encode("utf-8"))
        return base64.b64encode(nonce).decode("ascii"), base64.b64encode(data).decode("ascii")

    # None = alta parola, sau mesajul a fost modificat pe drum.
    def decrypt(self, nonce, data, author):
        try:
            nonce_bytes = base64.b64decode(nonce, validate=True)
            all_bytes = base64.b64decode(data, validate=True)
        except ValueError:
            return None   # nu era base64
        if len(nonce_bytes) != NONCE_SIZE or len(all_bytes) < TAG_SIZE:
            return None
        try:
            plain = self._aes.decrypt(nonce_bytes, all_bytes, author.encode("utf-8"))
            return plain.decode("utf-8")
        except (InvalidTag, UnicodeDecodeError):
            return None   # tag-ul nu bate
