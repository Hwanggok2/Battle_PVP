var ROOM_REGISTRY_ID = "GLOBAL_ROOM_REGISTRY";
var ROOM_STARTED_KEY = "IsStarted";
var ROOM_PLAYER_COUNT_KEY = "PlayerCount";
var ROOM_MASTER_NAME_KEY = "MasterName";

handlers.RegisterRoomToRegistry = function(args, context) {
    var roomId = requireString(args, "roomId");
    var roomName = requireString(args, "roomName");
    var masterName = getString(args, "masterName", "Unknown");

    ensureSharedGroup(ROOM_REGISTRY_ID);

    var roomInfo = {
        roomName: roomName,
        masterName: masterName,
        playerCount: 1
    };

    setRegistryRoomInfo(roomId, roomInfo);

    return {
        ok: true,
        roomId: roomId,
        roomName: roomName,
        roomInfo: roomInfo
    };
};

handlers.GetActiveRooms = function(args, context) {
    var roomInfos = loadActiveRoomInfos();
    var rooms = {};

    for (var roomId in roomInfos) {
        if (roomInfos.hasOwnProperty(roomId)) {
            rooms[roomId] = roomInfos[roomId].roomName;
        }
    }

    return {
        rooms: rooms
    };
};

handlers.GetActiveRoomInfos = function(args, context) {
    return {
        roomInfos: loadActiveRoomInfos()
    };
};

handlers.JoinRoom = function(args, context) {
    var roomId = requireString(args, "roomId");
    var roomInfo = getRoomInfo(roomId);

    if (!roomInfo) {
        throw "Room does not exist: " + roomId;
    }

    addCurrentPlayerToRoom(roomId);

    var roomData = getSharedGroupData(roomId);
    var currentCount = parseCount(getRecordValue(roomData, ROOM_PLAYER_COUNT_KEY), roomInfo.playerCount);
    var nextCount = Math.max(1, currentCount + 1);
    roomInfo.playerCount = nextCount;

    server.UpdateSharedGroupData({
        SharedGroupId: roomId,
        Data: buildRoomData(roomInfo)
    });

    setRegistryRoomInfo(roomId, roomInfo);

    return {
        ok: true,
        roomId: roomId,
        roomInfo: roomInfo
    };
};

handlers.LeaveRoom = function(args, context) {
    var roomId = requireString(args, "roomId");
    var roomInfo = getRoomInfo(roomId);

    if (!roomInfo) {
        removeRegistryRoom(roomId);
        return {
            ok: true,
            roomId: roomId,
            playerCount: 0
        };
    }

    var roomData = getSharedGroupData(roomId);
    var currentCount = parseCount(getRecordValue(roomData, ROOM_PLAYER_COUNT_KEY), roomInfo.playerCount);
    var nextCount = Math.max(0, currentCount - 1);
    roomInfo.playerCount = nextCount;

    if (nextCount <= 0) {
        removeRegistryRoom(roomId);
    } else {
        server.UpdateSharedGroupData({
            SharedGroupId: roomId,
            Data: buildRoomData(roomInfo)
        });
        setRegistryRoomInfo(roomId, roomInfo);
    }

    try {
        server.RemoveSharedGroupMembers({
            SharedGroupId: roomId,
            PlayFabIds: [currentPlayerId]
        });
    } catch (e) {
        log.info("RemoveSharedGroupMembers skipped for " + roomId + ": " + String(e));
    }

    return {
        ok: true,
        roomId: roomId,
        playerCount: nextCount
    };
};

function loadActiveRoomInfos() {
    ensureSharedGroup(ROOM_REGISTRY_ID);

    var registryData = getSharedGroupData(ROOM_REGISTRY_ID);
    var roomInfos = {};
    var dirtyRemoveKeys = [];

    for (var roomId in registryData) {
        if (!registryData.hasOwnProperty(roomId))
            continue;

        var registryValue = registryData[roomId] ? registryData[roomId].Value : null;
        var roomInfo = parseRegistryRecord(registryValue);
        if (!roomInfo)
            roomInfo = { roomName: roomId, masterName: "Unknown", playerCount: 1 };

        var roomData = getSharedGroupData(roomId);
        var hasRoomCountRecord = roomData && roomData[ROOM_PLAYER_COUNT_KEY];
        if (!hasRoomCountRecord && !isJsonObjectText(registryValue)) {
            dirtyRemoveKeys.push(roomId);
            continue;
        }

        if (roomData) {
            roomInfo.playerCount = parseCount(getRecordValue(roomData, ROOM_PLAYER_COUNT_KEY), roomInfo.playerCount);
            roomInfo.masterName = getRecordValue(roomData, ROOM_MASTER_NAME_KEY) || roomInfo.masterName || "Unknown";
        }

        if (roomInfo.playerCount <= 0) {
            dirtyRemoveKeys.push(roomId);
            continue;
        }

        roomInfos[roomId] = normalizeRoomInfo(roomInfo, roomId);
    }

    if (dirtyRemoveKeys.length > 0)
        removeRegistryRooms(dirtyRemoveKeys);

    return roomInfos;
}

function getRoomInfo(roomId) {
    ensureSharedGroup(ROOM_REGISTRY_ID);

    var registryData = getSharedGroupData(ROOM_REGISTRY_ID);
    if (!registryData || !registryData[roomId])
        return null;

    var roomInfo = parseRegistryRecord(registryData[roomId].Value);
    if (!roomInfo)
        roomInfo = { roomName: registryData[roomId].Value || roomId, masterName: "Unknown", playerCount: 1 };

    var roomData = getSharedGroupData(roomId);
    if (roomData) {
        roomInfo.playerCount = parseCount(getRecordValue(roomData, ROOM_PLAYER_COUNT_KEY), roomInfo.playerCount);
        roomInfo.masterName = getRecordValue(roomData, ROOM_MASTER_NAME_KEY) || roomInfo.masterName || "Unknown";
    }

    return normalizeRoomInfo(roomInfo, roomId);
}

function addCurrentPlayerToRoom(roomId) {
    try {
        server.AddSharedGroupMembers({
            SharedGroupId: roomId,
            PlayFabIds: [currentPlayerId]
        });
    } catch (e) {
        var message = String(e);
        if (message.indexOf("already") === -1 &&
            message.indexOf("MemberAlreadyExists") === -1) {
            throw e;
        }
    }
}

function setRegistryRoomInfo(roomId, roomInfo) {
    ensureSharedGroup(ROOM_REGISTRY_ID);

    var data = {};
    data[roomId] = JSON.stringify(normalizeRoomInfo(roomInfo, roomId));

    server.UpdateSharedGroupData({
        SharedGroupId: ROOM_REGISTRY_ID,
        Data: data,
        Permission: "Public"
    });
}

function removeRegistryRoom(roomId) {
    removeRegistryRooms([roomId]);
}

function removeRegistryRooms(roomIds) {
    if (!roomIds || roomIds.length === 0)
        return;

    ensureSharedGroup(ROOM_REGISTRY_ID);
    server.UpdateSharedGroupData({
        SharedGroupId: ROOM_REGISTRY_ID,
        KeysToRemove: roomIds
    });
}

function buildRoomData(roomInfo) {
    return {
        IsStarted: "false",
        PlayerCount: String(Math.max(0, roomInfo.playerCount || 0)),
        MasterName: roomInfo.masterName || "Unknown"
    };
}

function getSharedGroupData(groupId) {
    try {
        var response = server.GetSharedGroupData({
            SharedGroupId: groupId
        });

        return response && response.Data ? response.Data : {};
    } catch (e) {
        return {};
    }
}

function ensureSharedGroup(groupId) {
    try {
        server.CreateSharedGroup({
            SharedGroupId: groupId
        });
    } catch (e) {
        var message = String(e);
        if (message.indexOf("already") === -1 &&
            message.indexOf("exists") === -1 &&
            message.indexOf("NameNotAvailable") === -1) {
            log.info("CreateSharedGroup skipped or failed for " + groupId + ": " + message);
        }
    }
}

function parseRegistryRecord(value) {
    if (!value)
        return null;

    try {
        var parsed = JSON.parse(value);
        if (typeof parsed === "object" && parsed !== null)
            return parsed;
    } catch (e) {
        return {
            roomName: String(value),
            masterName: "Unknown",
            playerCount: 1
        };
    }

    return null;
}

function isJsonObjectText(value) {
    if (!value)
        return false;

    var text = String(value).trim();
    return text.indexOf("{") === 0;
}

function normalizeRoomInfo(roomInfo, roomId) {
    return {
        roomName: roomInfo.roomName || roomId || "Unnamed Room",
        masterName: roomInfo.masterName || "Unknown",
        playerCount: Math.max(0, parseCount(roomInfo.playerCount, 0))
    };
}

function getRecordValue(data, key) {
    return data && data[key] ? data[key].Value : null;
}

function parseCount(value, fallback) {
    var parsed = parseInt(value, 10);
    if (isNaN(parsed))
        parsed = parseInt(fallback, 10);
    if (isNaN(parsed))
        parsed = 0;

    return Math.max(0, parsed);
}

function getString(args, key, fallback) {
    if (!args || args[key] === undefined || args[key] === null)
        return fallback;

    var value = String(args[key]).trim();
    return value.length > 0 ? value : fallback;
}

function requireString(args, key) {
    var value = getString(args, key, "");
    if (value.length === 0)
        throw key + " is required.";

    return value;
}
