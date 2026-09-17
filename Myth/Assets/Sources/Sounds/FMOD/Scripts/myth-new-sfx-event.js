// Adds Myth > New SFX Event to FMOD Studio's menus (Scripts > Reload after editing). Creates the
// common case in one step -- spatialized, routed to Gameplay/SFX, in the SFX bank -- so a new sound
// can't be left on the master bus or out of every bank. Created next to the selected event/folder.
studio.menu.addMenuItem({
    name: "Myth\\New SFX Event",
    execute: function() {
        var sfx = studio.system.require(studio.project.filePath.replace(/[\\\/][^\\\/]*$/, "") + "/Scripts/modules/sfx-event.js");
        var selected = studio.window.browserCurrent();
        var folder = selected && selected.isOfType("EventFolder") ? selected
            : selected && selected.isOfType("Event") ? selected.folder
            : studio.project.workspace.masterEventFolder;

        var name = studio.system.getText("New SFX event name", "new_sfx");
        if (!name)
            return;
        studio.window.navigateTo(sfx.createEventIn(folder, name));
    }
});
