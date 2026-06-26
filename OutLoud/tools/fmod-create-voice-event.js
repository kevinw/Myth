// OutLoud — build (idempotent) one FMOD event per voice from the bake.
//
//   /Applications/FMOD\ Studio.app/Contents/MacOS/fmodstudiocl \
//     -script OutLoud/tools/fmod-create-voice-event.js Myth/Assets/Sounds/FMOD/Myth.fspro
//
// Creates event:/voice/<Name> for each entry in VOICES, each with local "syllable" (labeled,
// from manifest.names) and "pitch" parameters and that voice's clips from Assets/Voice/<Name>/.
// Re-run after re-baking / adding a voice. The pitch automation per event is the one manual
// step (master Pitch dial > Add Automation > pitch, linear -12..+12 st).

var VOICES = ["Kev"]; // fallback if voices.json is absent; make-voice.sh writes the real list

function up(p) { return p.substr(0, p.lastIndexOf("/")); }
var MANIFEST = up(up(up(studio.project.filePath))) + "/Voice/manifest.json"; // Myth/Assets/Voice
var CLIP_ROOT = up(studio.project.filePath) + "/Assets/Voice";                // inside FMOD tree
var CLIP_REL = "Voice";                                                        // rel to Assets dir
var FOLDER_PATH = "event:/voice";
var FOLDER_NAME = "voice";
var BANK_PATH = "bank:/SFX";

function log(m) { console.log(m); }

function readJson(path) {
	var f = studio.system.getFile(path);
	f.open(studio.system.openMode.ReadOnly);
	var text = f.readText(f.size());
	f.close();
	return JSON.parse(text);
}

function readManifest() { return readJson(MANIFEST); }

function readVoices() {
	try {
		var voices = readJson(up(up(up(studio.project.filePath))) + "/Voice/voices.json");
		if (voices && voices.length) return voices;
	} catch (e) {}
	return VOICES;
}

// importAudioFile needs the clip inside the project's Assets tree; try absolute then relative.
function importClip(voice, name) {
	return studio.project.importAudioFile(CLIP_ROOT + "/" + voice + "/" + name + ".ogg")
		|| studio.project.importAudioFile(CLIP_REL + "/" + voice + "/" + name + ".ogg");
}

// Delete orphaned "<base>"/"<base> (n)" parameter presets so re-runs don't pile up duplicates.
// Run only after the events referencing them are deleted.
function deletePresets(base) {
	try {
		var all = studio.project.model.ParameterPreset.findInstances();
		var deleted = 0;
		for (var i = 0; i < all.length; i++) {
			var nm = all[i].name;
			if (nm === base || (nm && nm.indexOf(base + " (") === 0)) { studio.project.deleteObject(all[i]); deleted++; }
		}
		log("deletePresets(" + base + "): " + all.length + " found, deleted " + deleted);
	} catch (e) { log("preset cleanup skipped (" + base + "): " + e); }
}

function eventFolder() {
	var folder = studio.project.lookup(FOLDER_PATH);
	if (!folder) { folder = studio.project.create("EventFolder"); folder.name = FOLDER_NAME; }
	return folder;
}

function buildVoice(voice, manifest, folder) {
	var event = studio.project.create("Event");
	event.name = voice;
	event.folder = folder;

	// Enumeration labels must be supplied at creation (FMOD reads their length then).
	var syllableParam = event.addGameParameter({ name: "syllable", type: studio.project.parameterType.UserEnumeration, enumerationLabels: manifest.names });
	event.addGameParameter({ name: "pitch", type: studio.project.parameterType.User, min: -12, max: 12 });

	var track = event.addGroupTrack();
	var missing = 0;
	for (var i = 0; i < manifest.count; i++) {
		var asset = importClip(voice, manifest.names[i]);
		if (!asset) { missing++; if (missing === 1) log("import null for " + voice + "/" + manifest.names[i] + ".ogg"); }
		var snd = track.addSound(syllableParam, "SingleSound", i, 1); // 1-wide region [i, i+1)
		snd.audioFile = asset;
	}

	var bank = studio.project.lookup(BANK_PATH);
	if (bank) event.relationships.banks.add(bank);
	log("Built event:/voice/" + voice + " (" + manifest.count + " syllables, " + missing + " missing)");
}

function build() {
	var manifest = readManifest();
	var voices = readVoices();

	for (var i = 0; i < voices.length; i++) {
		var existing = studio.project.lookup("event:/voice/" + voices[i]);
		if (existing) studio.project.deleteObject(existing);
	}
	deletePresets("syllable");
	deletePresets("pitch");

	var folder = eventFolder();
	for (var j = 0; j < voices.length; j++)
		buildVoice(voices[j], manifest, folder);

	studio.project.save();
	log("Done: " + voices.length + " voice event(s) [" + voices.join(", ") + "]. Add pitch automation per event by hand.");
}

build();
