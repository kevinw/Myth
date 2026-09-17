var sfx = studio.system.require(studio.project.filePath.replace(/[\\\/][^\\\/]*$/, "") + "/Scripts/modules/sfx-event.js");

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

            event = sfx.createEvent(request.eventPath);
            var sound = event.masterTrack.addSound(event.timeline, "SingleSound", 0, request.duration);
            sound.audioFile = audioFile;
        }

        if (!studio.project.save())
            throw new Error("Could not save the FMOD project.");
        studio.system.print("Synced " + eventPath + " from " + request.wavPath);
    }
};
