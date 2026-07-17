var ROOM_REGISTRY_ID = "GLOBALROOMREGISTRY";
var ROOM_STARTED_KEY = "IsStarted";
var ROOM_PLAYER_COUNT_KEY = "PlayerCount";
var ROOM_MASTER_NAME_KEY = "MasterName";
var REMOVE_KEYS_BATCH_SIZE = 10;

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

    ensureSharedGroup(roomId);
    server.UpdateSharedGroupData({
        SharedGroupId: roomId,
        Data: buildRoomData(roomInfo)
    });
    setRegistryRoomInfo(roomId, roomInfo);
    addCurrentPlayerToRoom(roomId, true);
    log.info("Registered room " + roomId + " (" + roomName + ") to " + ROOM_REGISTRY_ID);

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
    var roomInfos = loadActiveRoomInfos();
    var roomCount = 0;
    for (var roomId in roomInfos) {
        if (roomInfos.hasOwnProperty(roomId))
            roomCount++;
    }
    log.info("GetActiveRoomInfos returned " + roomCount + " room(s).");

    return {
        roomInfos: roomInfos
    };
};

handlers.AdminValidateRoomKey = function(args, context) {
    requireAdminKey(args);

    return {
        ok: true
    };
};

handlers.AdminClearRoomRegistry = function(args, context) {
    requireAdminKey(args);
    ensureSharedGroup(ROOM_REGISTRY_ID);

    var registryData = getSharedGroupData(ROOM_REGISTRY_ID);
    var roomIds = [];
    for (var roomId in registryData) {
        if (registryData.hasOwnProperty(roomId))
            roomIds.push(roomId);
    }

    removeRegistryRooms(roomIds);

    return {
        ok: true,
        removedCount: roomIds.length,
        removedRoomIds: roomIds
    };
};

handlers.AdminDeleteRoom = function(args, context) {
    requireAdminKey(args);
    ensureSharedGroup(ROOM_REGISTRY_ID);

    var requestedRoomId = getString(args, "roomId", "");
    var requestedRoomName = getString(args, "roomName", "");
    if (requestedRoomId.length === 0 && requestedRoomName.length === 0)
        throw "roomId or roomName is required.";

    var roomInfos = loadActiveRoomInfos();
    var matchedRoomIds = [];

    for (var roomId in roomInfos) {
        if (!roomInfos.hasOwnProperty(roomId))
            continue;

        var roomInfo = roomInfos[roomId];
        if (requestedRoomId.length > 0 && roomId === requestedRoomId) {
            matchedRoomIds.push(roomId);
            continue;
        }

        if (requestedRoomName.length > 0 && roomInfo.roomName === requestedRoomName)
            matchedRoomIds.push(roomId);
    }

    if (matchedRoomIds.length === 0)
        throw "No matching room found.";

    removeRegistryRooms(matchedRoomIds);

    return {
        ok: true,
        removedCount: matchedRoomIds.length,
        removedRoomIds: matchedRoomIds
    };
};

handlers.JoinRoom = function(args, context) {
    var roomId = requireString(args, "roomId");
    var roomInfo = getRoomInfo(roomId);

    if (!roomInfo) {
        throw "Room does not exist: " + roomId;
    }

    var memberAdded = addCurrentPlayerToRoom(roomId);

    var currentCount = Math.max(1, roomInfo.playerCount || 1);
    var nextCount = memberAdded ? Math.max(1, currentCount + 1) : Math.max(1, currentCount);
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

function addCurrentPlayerToRoom(roomId, ignoreFailure) {
    try {
        server.AddSharedGroupMembers({
            SharedGroupId: roomId,
            PlayFabIds: [currentPlayerId]
        });
        return true;
    } catch (e) {
        var message = getErrorText(e);
        if (message.indexOf("already") === -1 &&
            message.indexOf("Already") === -1 &&
            message.indexOf("MemberAlreadyExists") === -1 &&
            message.indexOf("UsersAlreadyInSharedGroup") === -1) {
            if (ignoreFailure) {
                log.info("AddSharedGroupMembers skipped for " + roomId + ": " + message);
                return false;
            }

            throw e;
        }

        return false;
    }
}

function setRegistryRoomInfo(roomId, roomInfo) {
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

    for (var i = 0; i < roomIds.length; i += REMOVE_KEYS_BATCH_SIZE) {
        var batch = roomIds.slice(i, i + REMOVE_KEYS_BATCH_SIZE);
        server.UpdateSharedGroupData({
            SharedGroupId: ROOM_REGISTRY_ID,
            KeysToRemove: batch
        });
    }
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
        var message = getErrorText(e);
        if (message.indexOf("already") === -1 &&
            message.indexOf("exists") === -1 &&
            message.indexOf("SharedGroupAlreadyExists") === -1 &&
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

function requireAdminKey(args) {
    var providedKey = getString(args, "adminKey", "");
    var expectedKey = getConfiguredAdminKey();

    if (!expectedKey)
        throw "RoomAdminKey is not configured in Title Data or Title Internal Data.";

    if (providedKey !== expectedKey)
        throw "Invalid adminKey.";
}

function getConfiguredAdminKey() {
    var internalKey = getTitleDataValue("RoomAdminKey", true);
    if (internalKey)
        return internalKey;

    return getTitleDataValue("RoomAdminKey", false);
}

function getTitleDataValue(key, internal) {
    try {
        var response = internal
            ? server.GetTitleInternalData({ Keys: [key] })
            : server.GetTitleData({ Keys: [key] });

        var rawValue = response && response.Data ? response.Data[key] : null;
        if (!rawValue)
            return "";

        if (rawValue.Value !== undefined && rawValue.Value !== null)
            return String(rawValue.Value);

        return String(rawValue);
    } catch (e) {
        log.info((internal ? "GetTitleInternalData" : "GetTitleData") + " failed: " + getErrorText(e));
        return "";
    }
}

function getErrorText(error) {
    if (!error)
        return "";

    if (typeof error === "string")
        return error;

    try {
        return JSON.stringify(error);
    } catch (e) {
        return String(error);
    }
}
