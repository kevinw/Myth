function requireObject(path) {
    var object = studio.project.lookup(path);
    if (!object)
        throw new Error("FMOD object not found: " + path);
    return object;
}

module.exports = {
    createOneShot: function(request) {
        var eventPath = "event:/" + request.eventPath;
        if (studio.project.lookup(eventPath))
            throw new Error("FMOD event already exists: " + eventPath);

        var audioFile = studio.project.importAudioFile(request.wavPath);
        if (!audioFile)
            throw new Error("Could not import audio file: " + request.wavPath);

        var event = studio.project.workspace.addEvent(request.eventPath, false);
        var sound = event.masterTrack.addSound(event.timeline, "SingleSound", 0, request.duration);
        sound.audioFile = audioFile;
        event.mixerInput.output = requireObject("bus:/Gameplay/SFX");
        event.relationships.banks.add(requireObject("bank:/SFX"));

        if (!studio.project.save())
            throw new Error("Could not save the FMOD project.");
        studio.system.print("Created " + eventPath + " from " + request.wavPath);
    }
};
