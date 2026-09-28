var ROOM_REGISTRY_ID = "GLOBALROOMREGISTRY";
var ROOM_STARTED_KEY = "IsStarted";
var ROOM_PLAYER_COUNT_KEY = "PlayerCount";
var ROOM_PLAYER_CAPACITY = 8;
var ROOM_MASTER_NAME_KEY = "MasterName";
var ROOM_RELAY_JOIN_CODE_KEY = "RelayJoinCode";
var REMOVE_KEYS_BATCH_SIZE = 10;
var ROOM_ID_PATTERN = /^battle_([a-f0-9]+)_([a-f0-9]{32})$/;
var ROOM_LEASE_PREFIX = "ROOMLEASE_";
var ROOM_CLOSED_PREFIX = "ROOMCLOSED_";
var ROOM_LEASE_TTL_MS = 60000;

// These checks require Client Shared Group mutations to be denied by API Access Policy.
// Existing GLOBALROOMREGISTRY members must also be removed outside this script.
// Shared Group writes are not a transaction and cannot implement atomic match rewards.

handlers.RegisterRoomToRegistry = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var roomName = requireString(args, "roomName");
    var masterName = getString(args, "masterName", "Unknown");
    var relayJoinCode = getString(args, "relayJoinCode", "");
    ensureSharedGroup(ROOM_REGISTRY_ID);
    var registryData = readRoomRegistry();
    var lease = readRoomLease(registryData, roomId);
    var existed = Object.prototype.hasOwnProperty.call(registryData, roomId) ||
        Object.prototype.hasOwnProperty.call(registryData, ROOM_LEASE_PREFIX + roomId) ||
        Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + roomId);
    if (existed) {
        requireLiveLease(lease);
    } else {
        // Only a never-used ID receives its initial lease. Closed/expired IDs remain tombstoned.
        lease = writeRoomLease(roomId);
    }
    var roomInfo = existed ? readRegisteredRoomInfo(registryData, roomId) : null;
    roomInfo = roomInfo || { roomName: roomName, masterName: masterName, playerCount: 1, relayJoinCode: relayJoinCode };
    roomInfo.relayJoinCode = relayJoinCode;
    ensureSharedGroup(roomId);
    roomInfo.playerCount = joinRoomWithinCapacity(roomId);
    server.UpdateSharedGroupData({ SharedGroupId: roomId, Data: buildRoomData(roomInfo) });
    setRegistryRoomInfo(roomId, roomInfo);
    // A concurrent explicit closure always wins, including over late registration metadata.
    lease = readRoomLease(readRoomRegistry(), roomId);
    requireLiveLease(lease);
    return {
        ok: true, roomId: roomId, roomName: roomName, roomInfo: withRoomLease(roomInfo, lease),
        serverNow: lease.serverNow, leaseExpiresAt: lease.expiresAt
    };
};

handlers.HeartbeatRoom = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var registryData = readRoomRegistry();
    var lease = readRoomLease(registryData, roomId);
    requireLiveLease(lease);
    if (!Object.prototype.hasOwnProperty.call(registryData, roomId))
        throw "Room is not registered.";
    requireRoomMember(roomId, requireAuthenticatedPlayerId());
    lease = writeRoomLease(roomId);
    // This only writes the lease key. It never removes or overwrites a CLOSED tombstone.
    // Shared Group storage has no CAS: a concurrent closure may finish after this read.
    lease = readRoomLease(readRoomRegistry(), roomId);
    requireLiveLease(lease);
    return { ok: true, roomId: roomId, serverNow: lease.serverNow, leaseExpiresAt: lease.expiresAt };
};

handlers.UpdateRoomRelayJoinCode = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var relayJoinCode = requireString(args, "relayJoinCode");
    var roomInfo = getRoomInfo(roomId);

    if (!roomInfo)
        throw "Room does not exist: " + roomId;

    roomInfo.relayJoinCode = relayJoinCode;

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

// This short-lived approval is not a bearer credential and is never written to public room data.
// One active approval attempt per account; connection replay prevention belongs to the trusted host.
var ROOM_CONNECTION_PROOF_KEY = "BattleRoomConnectionProof";
var ROOM_CONNECTION_PROOF_TTL_MS = 60000;

handlers.ApproveRoomConnection = function(args, context) {
    var roomId = requireMutableRoomId(args);
    var playerId = requireAuthenticatedPlayerId();
    if (playerId.length > 64) throw "A valid participant account is required.";
    var challenge = requireRoomChallenge(args);
    if (!getRoomInfo(roomId)) throw "Room does not exist.";
    requireNotKicked(roomId, playerId);
    requireRoomMember(roomId, playerId);
    var proof = {
        version: 1, roomId: roomId, playerId: playerId, challenge: challenge,
        expiresAt: Date.now() + ROOM_CONNECTION_PROOF_TTL_MS
    };
    var data = {};
    data[ROOM_CONNECTION_PROOF_KEY] = JSON.stringify(proof);
    server.UpdateUserInternalData({ PlayFabId: currentPlayerId, Data: data });
    return { ok: true, roomId: roomId, playerId: playerId, challenge: challenge };
};

handlers.VerifyRoomConnection = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var playerId = requireString(args, "playerId").toLowerCase();
    if (playerId.length > 64 || !/^[a-f0-9]+$/.test(playerId))
        throw "A valid participant account is required.";
    var challenge = requireRoomChallenge(args);
    if (!getRoomInfo(roomId)) throw "Room does not exist.";
    requireNotKicked(roomId, playerId);
    requireRoomMember(roomId, requireAuthenticatedPlayerId());
    requireRoomMember(roomId, playerId);
    var response = server.GetUserInternalData({ PlayFabId: playerId, Keys: [ROOM_CONNECTION_PROOF_KEY] });
    var raw = response && response.Data ? response.Data[ROOM_CONNECTION_PROOF_KEY] : null;
    var proof = null;
    try { proof = raw && typeof raw.Value === "string" ? JSON.parse(raw.Value) : null; }
    catch (e) { throw "Connection approval is invalid."; }
    var now = Date.now();
    if (!proof || proof.version !== 1 || proof.roomId !== roomId || proof.playerId !== playerId ||
        proof.challenge !== challenge || typeof proof.expiresAt !== "number" ||
        !isFinite(proof.expiresAt) || proof.expiresAt <= now || proof.expiresAt > now + ROOM_CONNECTION_PROOF_TTL_MS)
        throw "Connection approval is absent, expired or does not match.";
    // No read-then-delete: it is not atomic and could erase a newer reconnect approval.
    // The host accepts this fresh challenge only once for its original pending connection.
    return { ok: true, roomId: roomId, playerId: playerId, challenge: challenge };
};

function requireRoomChallenge(args) {
    var challenge = requireString(args, "challenge");
    if (!/^[a-f0-9]{32}$/.test(challenge) || challenge === "00000000000000000000000000000000")
        throw "A fresh connection challenge is required.";
    return challenge;
}

function requireRoomMember(roomId, playerId) {
    var response = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true });
    var members = response.Members || [];
    for (var i = 0; i < members.length; i++)
        if (String(members[i]).toLowerCase() === playerId) return;
    throw "The authenticated player must have joined the room.";
}

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
    var registryData = readRoomRegistry();
    var selected = Object.create(null);
    for (var key in registryData) {
        if (!Object.prototype.hasOwnProperty.call(registryData, key)) continue;
        if (!isRoomStateKey(key)) selected[key] = true;
        else if (key.indexOf(ROOM_LEASE_PREFIX) === 0) {
            var roomId = key.substring(ROOM_LEASE_PREFIX.length);
            if (ROOM_ID_PATTERN.test(roomId) &&
                !Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + roomId))
                selected[roomId] = true;
        }
    }
    var roomIds = Object.keys(selected);
    removeRegistryRooms(roomIds);
    return { ok: true, removedCount: roomIds.length, removedRoomIds: roomIds };
};

handlers.AdminDeleteRoom = function(args, context) {
    requireAdminKey(args);
    ensureSharedGroup(ROOM_REGISTRY_ID);
    var requestedRoomId = getString(args, "roomId", "");
    var requestedRoomName = getString(args, "roomName", "");
    if (requestedRoomId.length === 0 && requestedRoomName.length === 0)
        throw "roomId or roomName is required.";
    var registryData = readRoomRegistry();
    var matchedRoomIds = [];
    if (requestedRoomId.length > 0 && !isRoomStateKey(requestedRoomId)) {
        if (Object.prototype.hasOwnProperty.call(registryData, requestedRoomId) ||
            (ROOM_ID_PATTERN.test(requestedRoomId) &&
                (Object.prototype.hasOwnProperty.call(registryData, ROOM_LEASE_PREFIX + requestedRoomId) ||
                 Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + requestedRoomId))))
            matchedRoomIds.push(requestedRoomId);
    } else if (requestedRoomName.length > 0) {
        for (var roomId in registryData) {
            if (!Object.prototype.hasOwnProperty.call(registryData, roomId) || isRoomStateKey(roomId)) continue;
            var info = parseRegistryRecord(registryData[roomId].Value);
            if (info && info.roomName === requestedRoomName) matchedRoomIds.push(roomId);
        }
    }
    if (matchedRoomIds.length === 0) throw "No matching room found.";
    removeRegistryRooms(matchedRoomIds);
    return { ok: true, removedCount: matchedRoomIds.length, removedRoomIds: matchedRoomIds };
};

handlers.JoinRoom = function(args, context) {
    var roomId = requireMutableRoomId(args);
    var roomInfo = getRoomInfo(roomId);

    if (!roomInfo) {
        throw "Room does not exist: " + roomId;
    }

    var playerId = requireAuthenticatedPlayerId();
    var settings = readRoomSettings(roomId);
    requireNotKicked(roomId, playerId);
    if (playerId !== ROOM_ID_PATTERN.exec(roomId)[1] && settings.passwordHash &&
        getString(args, "passwordHash", "") !== settings.passwordHash) throw "ROOM_PASSWORD_INVALID";
    var nextCount = joinRoomWithinCapacity(roomId, settings.capacity);
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

// Admission secrets and kick records are server-only; never copy them into room listings.
function readRoomInternal(roomId, key) {
    var owner = ROOM_ID_PATTERN.exec(roomId)[1];
    var response = server.GetUserInternalData({ PlayFabId: owner, Keys: [key] });
    var record = response && response.Data ? response.Data[key] : null;
    return record ? record.Value : null;
}
function writeRoomInternal(roomId, key, value) {
    var data = {}; data[key] = value;
    server.UpdateUserInternalData({ PlayFabId: ROOM_ID_PATTERN.exec(roomId)[1], Data: data });
}
function readRoomSettings(roomId) {
    var raw = readRoomInternal(roomId, "RoomSettings_" + roomId);
    return raw ? JSON.parse(raw) : { capacity: 8, passwordHash: "" };
}
function requireNotKicked(roomId, playerId) {
    if (readRoomInternal(roomId, "RoomKick_" + roomId + "_" + playerId)) throw "ROOM_KICKED";
}
handlers.UpdateRoomSettings = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var info = getRoomInfo(roomId);
    if (!info) throw "Room does not exist.";
    var title = requireString(args, "roomName");
    var capacity = args.capacity;
    if (title.length > 40 || typeof capacity !== "number" || capacity % 1 !== 0 || capacity < 2 || capacity > 8)
        throw "Invalid room settings.";
    var members = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true }).Members || [];
    if (capacity < members.length) throw "ROOM_CAPACITY_BELOW_MEMBERS";
    var settings = readRoomSettings(roomId);
    var passwordHash = getString(args, "passwordHash", "");
    if (args.isPrivate === true) {
        if (!passwordHash) passwordHash = settings.passwordHash;
        if (!/^[a-f0-9]{64}$/.test(passwordHash)) throw "ROOM_PASSWORD_REQUIRED";
    } else passwordHash = "";
    settings.capacity = capacity; settings.passwordHash = passwordHash;
    // Persist the admission rule before advertising it. A failed write must never report success.
    writeRoomInternal(roomId, "RoomSettings_" + roomId, JSON.stringify(settings));
    info.roomName = title; info.capacity = capacity; info.isPrivate = !!passwordHash;
    info.playerCount = members.length;
    setRegistryRoomInfo(roomId, info);
    return { ok: true, roomId: roomId, roomInfo: info };
};
handlers.KickRoomPlayer = function(args, context) {
    var roomId = requireOwnedRoomId(args);
    var playerId = requireString(args, "playerId").toLowerCase();
    if (!/^[a-f0-9]{1,64}$/.test(playerId) || playerId === requireAuthenticatedPlayerId()) throw "Invalid kick target.";
    var info = getRoomInfo(roomId);
    if (!info) throw "Room does not exist.";
    requireRoomMember(roomId, playerId);
    writeRoomInternal(roomId, "RoomKick_" + roomId + "_" + playerId, "true");
    server.RemoveSharedGroupMembers({ SharedGroupId: roomId, PlayFabIds: [playerId] });
    info.playerCount = (server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true }).Members || []).length;
    server.UpdateSharedGroupData({ SharedGroupId: roomId, Data: buildRoomData(info) });
    setRegistryRoomInfo(roomId, info);
    return { ok: true, roomId: roomId, roomInfo: info };
};

handlers.LeaveRoom = function(args, context) {
    var roomId = requireMutableRoomId(args);
    var registryData = readRoomRegistry(true);
    var knownRoom = Object.prototype.hasOwnProperty.call(registryData, roomId) ||
        Object.prototype.hasOwnProperty.call(registryData, ROOM_LEASE_PREFIX + roomId) ||
        Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + roomId);
    var ownerLeaving = ROOM_ID_PATTERN.exec(roomId)[1] === requireAuthenticatedPlayerId();
    // Closing the host's room must happen even if member removal later fails.
    if (ownerLeaving && knownRoom) removeRegistryRoom(roomId);
    var before;
    try { before = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true }); }
    catch (e) {
        if (!isMissingSharedGroup(e)) throw e;
        return { ok: true, roomId: roomId, playerCount: 0 };
    }
    var members = before.Members || [];
    if (!hasCurrentPlayer(members))
        return { ok: true, roomId: roomId, playerCount: members.length };
    server.RemoveSharedGroupMembers({ SharedGroupId: roomId, PlayFabIds: [currentPlayerId] });
    var nextCount;
    try {
        var after = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true });
        nextCount = (after.Members || []).length;
    } catch (e) {
        if (!isMissingSharedGroup(e)) throw e;
        nextCount = 0;
    }
    if (nextCount <= 0) {
        if (knownRoom) removeRegistryRoom(roomId);
    } else if (!ownerLeaving && readRoomLease(registryData, roomId).live) {
        var roomInfo = readRegisteredRoomInfo(registryData, roomId);
        if (roomInfo) {
            roomInfo.playerCount = nextCount;
            server.UpdateSharedGroupData({ SharedGroupId: roomId, Data: buildRoomData(roomInfo) });
            setRegistryRoomInfo(roomId, roomInfo);
        }
    }
    return { ok: true, roomId: roomId, playerCount: nextCount };
};

function readRoomRegistry(allowMissing) {
    // Only an actually absent registry is an empty initial list. Other service errors fail closed.
    try {
        var response = server.GetSharedGroupData({ SharedGroupId: ROOM_REGISTRY_ID });
        return response && response.Data ? response.Data : {};
    } catch (e) {
        if (allowMissing && isMissingSharedGroup(e)) return {};
        throw e;
    }
}

function isRoomStateKey(key) {
    return key.indexOf(ROOM_LEASE_PREFIX) === 0 || key.indexOf(ROOM_CLOSED_PREFIX) === 0;
}

function readRoomLease(registryData, roomId) {
    var rawExpiry = getRecordValue(registryData, ROOM_LEASE_PREFIX + roomId);
    var expiry = typeof rawExpiry === "string" && /^[0-9]+$/.test(rawExpiry) ? Number(rawExpiry) : NaN;
    var now = Date.now();
    return {
        expiresAt: expiry, serverNow: now,
        closed: Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + roomId),
        live: !Object.prototype.hasOwnProperty.call(registryData, ROOM_CLOSED_PREFIX + roomId) &&
            isFinite(expiry) && Math.floor(expiry) === expiry && expiry <= 9007199254740991 &&
            expiry > now && expiry <= now + ROOM_LEASE_TTL_MS
    };
}

function requireLiveLease(lease) {
    if (!lease || !lease.live) throw "Room is closed or its host lease has expired. Create a new room.";
}

function writeRoomLease(roomId) {
    var now = Date.now();
    var expiry = now + ROOM_LEASE_TTL_MS;
    var data = {};
    data[ROOM_LEASE_PREFIX + roomId] = String(expiry);
    server.UpdateSharedGroupData({ SharedGroupId: ROOM_REGISTRY_ID, Data: data, Permission: "Public" });
    return { expiresAt: expiry, serverNow: now, closed: false, live: true };
}

function withRoomLease(roomInfo, lease) {
    var result = normalizeRoomInfo(roomInfo);
    result.leaseExpiresAt = lease.expiresAt;
    result.serverNow = lease.serverNow;
    return result;
}

function loadActiveRoomInfos() {
    var registryData = readRoomRegistry(true);
    var roomInfos = {};
    for (var roomId in registryData) {
        if (!Object.prototype.hasOwnProperty.call(registryData, roomId) || isRoomStateKey(roomId)) continue;
        var lease = readRoomLease(registryData, roomId);
        if (!lease.live) continue;
        var roomInfo = readRegisteredRoomInfo(registryData, roomId);
        lease = readRoomLease(registryData, roomId);
        if (lease.live && roomInfo && roomInfo.playerCount > 0) roomInfos[roomId] = withRoomLease(roomInfo, lease);
    }
    // Read-only filtering. Expired records and CLOSED tombstones are deliberately retained.
    return roomInfos;
}

function readRegisteredRoomInfo(registryData, roomId) {
    if (!Object.prototype.hasOwnProperty.call(registryData, roomId) || isRoomStateKey(roomId)) return null;
    var roomInfo = parseRegistryRecord(registryData[roomId].Value);
    if (!roomInfo) roomInfo = { roomName: roomId, masterName: "Unknown", playerCount: 1 };
    var roomData = getSharedGroupData(roomId);
    roomInfo.playerCount = parseCount(getRecordValue(roomData, ROOM_PLAYER_COUNT_KEY), roomInfo.playerCount);
    roomInfo.masterName = getRecordValue(roomData, ROOM_MASTER_NAME_KEY) || roomInfo.masterName || "Unknown";
    roomInfo.relayJoinCode = getRecordValue(roomData, ROOM_RELAY_JOIN_CODE_KEY) || roomInfo.relayJoinCode || "";
    return normalizeRoomInfo(roomInfo, roomId);
}

function getRoomInfo(roomId) {
    var registryData = readRoomRegistry();
    requireLiveLease(readRoomLease(registryData, roomId));
    var roomInfo = readRegisteredRoomInfo(registryData, roomId);
    var lease = readRoomLease(registryData, roomId);
    requireLiveLease(lease);
    return roomInfo ? withRoomLease(roomInfo, lease) : null;
}

function joinRoomWithinCapacity(roomId, capacity) {
    capacity = capacity || ROOM_PLAYER_CAPACITY;
    var before = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true });
    var members = before.Members || [];
    if (hasCurrentPlayer(members))
        return members.length;
    if (members.length >= capacity)
        throw "Room is full (maximum 8 players).";

    addCurrentPlayerToRoom(roomId);
    var after = server.GetSharedGroupData({ SharedGroupId: roomId, GetMembers: true });
    var count = (after.Members || []).length;
    if (count > capacity) {
        // Concurrent joins require backend transaction support for strict reservation semantics.
        // Roll back this admission; Mirror and Relay independently enforce the final session cap.
        server.RemoveSharedGroupMembers({ SharedGroupId: roomId, PlayFabIds: [currentPlayerId] });
        throw "Room is full (maximum 8 players).";
    }
    return count;
}

function addCurrentPlayerToRoom(roomId) {
    try {
        server.AddSharedGroupMembers({ SharedGroupId: roomId, PlayFabIds: [currentPlayerId] });
    } catch (e) {
        var message = getErrorText(e);
        if (message.indexOf("already") === -1 &&
            message.indexOf("Already") === -1 &&
            message.indexOf("MemberAlreadyExists") === -1 &&
            message.indexOf("UsersAlreadyInSharedGroup") === -1)
            throw e;
    }
}

function requireAuthenticatedPlayerId() {
    if (typeof currentPlayerId !== "string" || !/^[a-fA-F0-9]+$/.test(currentPlayerId))
        throw "An authenticated PlayFab player is required.";
    return currentPlayerId.toLowerCase();
}

function requireMutableRoomId(args) {
    requireAuthenticatedPlayerId();
    var roomId = requireString(args, "roomId");
    if (!ROOM_ID_PATTERN.test(roomId))
        throw "Invalid or legacy room ID. Recreate the room before changing it.";
    return roomId;
}

function requireOwnedRoomId(args) {
    var roomId = requireMutableRoomId(args);
    var ownerId = ROOM_ID_PATTERN.exec(roomId)[1];
    if (ownerId !== requireAuthenticatedPlayerId())
        throw "Only the authenticated room owner may change this room.";
    return roomId;
}

function hasCurrentPlayer(members) {
    var playerId = requireAuthenticatedPlayerId();
    for (var i = 0; i < members.length; i++)
        if (String(members[i]).toLowerCase() === playerId)
            return true;
    return false;
}

function isMissingSharedGroup(error) {
    var details = error && error.apiErrorInfo ? error.apiErrorInfo : error;
    // Classic CloudScript wraps the API error object inside apiErrorInfo.apiError.
    if (details && details.apiError && typeof details.apiError === "object")
        details = details.apiError;
    if (details && (details.errorCode === 1088 || details.apiErrorCode === 1088))
        return true; // InvalidSharedGroupId: absent on read, or an ID collision on create.
    var name = typeof details === "string" ? details :
        details && (details.error || details.apiError || details.message);
    return name === "InvalidSharedGroupId" || name === "SharedGroupNotFound";
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
    if (!roomIds || roomIds.length === 0) return;
    var metadataKeys = [];
    for (var i = 0; i < roomIds.length; i++)
        if (!isRoomStateKey(roomIds[i])) metadataKeys.push(roomIds[i]);
    for (var offset = 0; offset < metadataKeys.length; offset += REMOVE_KEYS_BATCH_SIZE) {
        var batch = metadataKeys.slice(offset, offset + REMOVE_KEYS_BATCH_SIZE);
        var closed = {};
        for (var index = 0; index < batch.length; index++)
            if (ROOM_ID_PATTERN.test(batch[index])) closed[ROOM_CLOSED_PREFIX + batch[index]] = "true";
        // Never remove lease/CLOSED keys. These survive the automatic deletion of an empty room group.
        server.UpdateSharedGroupData({
            SharedGroupId: ROOM_REGISTRY_ID, Data: closed, KeysToRemove: batch, Permission: "Public"
        });
    }
}

function buildRoomData(roomInfo) {
    return {
        IsStarted: "false",
        PlayerCount: String(Math.max(0, roomInfo.playerCount || 0)),
        MasterName: roomInfo.masterName || "Unknown",
        RelayJoinCode: roomInfo.relayJoinCode || ""
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
        server.GetSharedGroupData({ SharedGroupId: groupId });
        return;
    } catch (e) {
        if (!isMissingSharedGroup(e)) throw e;
    }

    try {
        server.CreateSharedGroup({ SharedGroupId: groupId });
    } catch (e) {
        // Create also returns InvalidSharedGroupId (1088) when another request created it first.
        // Confirm it exists; never treat an invalid ID or a service failure as successful creation.
        if (!isMissingSharedGroup(e)) throw e;
        server.GetSharedGroupData({ SharedGroupId: groupId });
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
        playerCount: Math.max(0, parseCount(roomInfo.playerCount, 0)),
        relayJoinCode: roomInfo.relayJoinCode || "",
        capacity: Math.max(2, Math.min(8, parseCount(roomInfo.capacity, 8))),
        isPrivate: roomInfo.isPrivate === true
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
        throw "RoomAdminKey is not configured in Title Internal Data.";

    if (providedKey !== expectedKey)
        throw "Invalid adminKey.";
}

function getConfiguredAdminKey() {
    try {
        var response = server.GetTitleInternalData({ Keys: ["RoomAdminKey"] });
        var rawValue = response && response.Data ? response.Data.RoomAdminKey : null;
        if (!rawValue) return "";
        if (rawValue.Value !== undefined && rawValue.Value !== null)
            return String(rawValue.Value);
        return String(rawValue);
    } catch (e) {
        log.info("GetTitleInternalData failed; administrator access is denied.");
        return "";
    }
}

function getErrorText(error) {
    if (!error)
        return "";

    if (typeof error === "string")
        return error;

    if (typeof error.message === "string")
        return error.message;

    try {
        return JSON.stringify(error);
    } catch (e) {
        return String(error);
    }
}

// Only these game endpoints are callable. Tutorial handlers remain disabled even if bundled above.
var CLIENT_CLOUDSCRIPT_HANDLER_ALLOWLIST = [
    "RegisterRoomToRegistry", "UpdateRoomRelayJoinCode", "GetActiveRooms", "GetActiveRoomInfos",
    "JoinRoom", "LeaveRoom", "AdminValidateRoomKey", "AdminDeleteRoom", "AdminClearRoomRegistry",
    "ApproveRoomConnection", "VerifyRoomConnection", "HeartbeatRoom", "UpdateRoomSettings", "KickRoomPlayer"
];
for (var handlerName in handlers) {
    if (Object.prototype.hasOwnProperty.call(handlers, handlerName) &&
        CLIENT_CLOUDSCRIPT_HANDLER_ALLOWLIST.indexOf(handlerName) === -1)
        handlers[handlerName] = disabledCloudScriptHandler;
}

function disabledCloudScriptHandler(args, context) {
    throw "This CloudScript handler is disabled.";
}
