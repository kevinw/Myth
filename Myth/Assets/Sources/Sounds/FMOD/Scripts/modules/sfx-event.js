// What a gameplay sound effect is, in one place for Myth's FMOD scripts: spatialized, routed under
// Gameplay (so the pause menu and the SFX VCA reach it), and built into the SFX bank.

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

function configure(event) {
    event.masterTrack.mixerGroup.effectChain.addEffect("SpatialiserEffect");
    event.mixerInput.output = requireObject("bus:/Gameplay/SFX");
    event.relationships.banks.add(requireObject("bank:/SFX"));
}

module.exports = {
    configure: configure,

    // eventPath is relative to event:/, with folders created as needed ("boss/stomp").
    createEvent: function(eventPath) {
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
        return this.createEventIn(folder, eventName);
    },

    createEventIn: function(folder, name) {
        var event = studio.project.create("Event");
        event.name = name;
        event.folder = folder;
        configure(event);
        return event;
    }
};
