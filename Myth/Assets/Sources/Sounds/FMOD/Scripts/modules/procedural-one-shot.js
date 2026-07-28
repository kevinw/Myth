function requireObject(path) {
    var object = studio.project.lookup(path);
    if (!object)
        throw new Error("FMOD object not found: " + path);
    return object;
}

function findOrCreateEventFolder(parent, name) {
    for (var i = 0; i < parent.items.length; i++) {
        var item = parent.items[i];
        if (item.isOfType("EventFolder") && item.name === name)
            return item;
    }

    var folder = studio.project.create("EventFolder");
    folder.name = name;
    folder.folder = parent;
    return folder;
}

function createEvent(eventPath) {
    var parts = eventPath.split("/");
    var eventName = parts.pop();
    if (!eventName)
        throw new Error("Invalid FMOD event path: " + eventPath);

    var folder = studio.project.workspace.masterEventFolder;
    for (var i = 0; i < parts.length; i++) {
        if (!parts[i])
            throw new Error("Invalid FMOD event path: " + eventPath);
        folder = findOrCreateEventFolder(folder, parts[i]);
    }

    var event = studio.project.create("Event");
    event.name = eventName;
    event.folder = folder;
    return event;
}

module.exports = {
    syncOneShot: function(request) {
        var eventPath = "event:/" + request.eventPath;
        var event = studio.project.lookup(eventPath);
        if (event) {
            var audioFile = studio.project.workspace.masterAssetFolder.getAsset(request.assetPath);
            if (!audioFile)
                throw new Error("FMOD asset not found: " + request.assetPath);
            audioFile.refreshAudio();

            var sounds = studio.project.model.SingleSound.findInstances({ searchContext: event });
            if (sounds.length !== 1)
                throw new Error("Expected one single instrument in " + eventPath + ", found " + sounds.length);
            sounds[0].audioFile = audioFile;
            sounds[0].length = request.duration;
        } else {
            var audioFile = studio.project.importAudioFile(request.wavPath);
            if (!audioFile)
                throw new Error("Could not import audio file: " + request.wavPath);

            event = createEvent(request.eventPath);
            var sound = event.masterTrack.addSound(event.timeline, "SingleSound", 0, request.duration);
            sound.audioFile = audioFile;
            event.masterTrack.mixerGroup.effectChain.addEffect("SpatialiserEffect");
            event.mixerInput.output = requireObject("bus:/Gameplay/SFX");
            event.relationships.banks.add(requireObject("bank:/SFX"));
        }

        if (!studio.project.save())
            throw new Error("Could not save the FMOD project.");
        studio.system.print("Synced " + eventPath + " from " + request.wavPath);
    }
};
