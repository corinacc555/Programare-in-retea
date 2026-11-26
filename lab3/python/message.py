import json

# Formatul comun cu chat-ul in C# (Message.cs): o datagrama = un obiect JSON, ex:
#   {"type":"general","from":"Ana","id":"3f2a9c1b","text":"salut"}
# Numele campurilor si tipurile trebuie sa fie EXACT aceleasi, altfel cele doua chat-uri nu se inteleg.

JOIN = "join"        # multicast: am intrat
HERE = "here"        # unicast, raspuns la join: si eu sunt aici
TAKEN = "taken"      # unicast, raspuns la join: numele e deja luat
ALIVE = "alive"      # multicast, la fiecare 5 s
BYE = "bye"          # multicast: am iesit cu /quit
GENERAL = "general"  # multicast: mesaj pentru toti
PRIVATE = "private"  # unicast, criptat

ALL_TYPES = {JOIN, HERE, TAKEN, ALIVE, BYE, GENERAL, PRIVATE}

MAX_BYTES = 4096
MAX_TEXT_LENGTH = 500
MAX_NAME_LENGTH = 20


class Message:
    # "from" e cuvant rezervat in Python, de aceea campul se numeste sender
    def __init__(self, type, sender, id, text=None, nonce=None, size=0):
        self.type = type
        self.sender = sender
        self.id = id
        self.text = text      # la private: textul criptat, in base64
        self.nonce = nonce    # doar la private
        self.size = size      # cati bytes a avut datagrama, pentru log

    def to_bytes(self):
        data = {"type": self.type, "from": self.sender, "id": self.id}
        # campurile goale nu se trimit deloc, la fel ca in C#
        if self.text is not None:
            data["text"] = self.text
        if self.nonce is not None:
            data["nonce"] = self.nonce
        return json.dumps(data).encode("utf-8")


# Intoarce (mesaj, "") daca datagrama e un mesaj valid al chatului,
# sau (None, motiv) daca trebuie aruncata.
def parse(data):
    if len(data) > MAX_BYTES:
        return None, "too long"

    fields = _try_load(data)
    if fields is None:
        return None, "not a chat message"

    problem = _find_problem(fields)
    if problem != "":
        return None, problem

    message = Message(fields["type"], fields["from"], fields["id"],
                      fields.get("text"), fields.get("nonce"), len(data))
    return message, ""


def _try_load(data):
    try:
        fields = json.loads(data.decode("utf-8"))
    except ValueError:
        return None   # bytes random, alt program, json stricat, utf-8 invalid
    if not isinstance(fields, dict):
        return None   # json corect, dar nu un obiect (ex: o lista sau un numar)
    return fields


# json corect nu inseamna mesaj corect: verific campurile de care am nevoie
def _find_problem(fields):
    type = fields.get("type")
    sender = fields.get("from")
    id = fields.get("id")
    text = fields.get("text")
    nonce = fields.get("nonce")

    if type not in ALL_TYPES:
        return "unknown type"
    if not isinstance(sender, str) or sender == "" or len(sender) > MAX_NAME_LENGTH:
        return "bad name"
    if not isinstance(id, str) or id == "":
        return "no id"
    if text is not None and not isinstance(text, str):
        return "bad text"
    if nonce is not None and not isinstance(nonce, str):
        return "bad nonce"
    if type == GENERAL and (text is None or len(text) > MAX_TEXT_LENGTH):
        return "bad text"
    if type == PRIVATE and (text is None or nonce is None):
        return "bad private"
    return ""
