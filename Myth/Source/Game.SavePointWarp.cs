using System.Numerics;
using Foster.Framework;

namespace Myth;

public partial class Game
{
	enum SavePointWarpPhase
	{
		None,
		Exit,
		FadeOut,
		Black,
		Enter,
	}

	static float SavePointWarpExitTime => 0.9f;
	static float SavePointWarpFadeOutTime => 0.38f;
	static float SavePointWarpBlackTime => 0.29f;
	static float SavePointWarpEnterTime => 0.75f;
	static float SavePointWarpGlitchAmount => 0.85f;
	static float SavePointWarpParticlesPerSecond => 99f;
	static Vector2 SavePointWarpSquish => new(0.2f, 1.45f);

	SavePointWarpPhase savePointWarpPhase;
	Minimap.WarpTarget savePointWarpTarget;
	float savePointWarpTime;
	float savePointWarpParticleRemainder;
	int savePointWarpParticleSlot = -1;
	Vector2 savePointWarpPlayerSquish;
	Vector2 savePointWarpBudSquish;

	internal bool SavePointWarpActive => savePointWarpPhase != SavePointWarpPhase.None;
	internal float SavePointWarpFade { get; private set; }

	void BeginSavePointWarp(Minimap.WarpTarget target)
	{
		if (SavePointWarpActive || GetFirst<Player>() is not { } player ||
			(room?.Id != target.RoomId && Assets.GetRoom(target.RoomId) == null))
			return;


		PlayAudioEvent("teleport_from");

		savePointWarpTarget = target;
		savePointWarpPhase = SavePointWarpPhase.Exit;
		savePointWarpTime = 0;
		savePointWarpParticleRemainder = 0;
		SavePointWarpFade = 0;
		savePointWarpPlayerSquish = player.Squish;
		savePointWarpBudSquish = GetFirst<Bud>()?.Squish ?? Vector2.One;

		player.State = Player.States.Normal;
		player.Stop();
		GetFirst<Bud>()?.Stop();

		ditherGlitchTarget = 0;
		ditherGlitchAxis = Vector2.UnitY;
		ditherGlitchPosition = player.Position;

		if (savePointWarpParticleSlot < 0 && Particles is { Supported: true } particles)
			savePointWarpParticleSlot = particles.AcquireEffect("teleport_rise");
	}

	bool UpdateSavePointWarp()
	{
		if (!SavePointWarpActive)
			return false;

		var dt = Manager.Time.Delta;
		savePointWarpTime += dt;

		switch (savePointWarpPhase)
		{
			case SavePointWarpPhase.Exit:
			{
				var t = Math.Clamp(savePointWarpTime / SavePointWarpExitTime, 0, 1);
				ApplySavePointWarpPose(t, exiting: true);
				EmitSavePointWarpParticles(dt);
				SetSavePointWarpGlitch(Ease.Cube.In(t) * SavePointWarpGlitchAmount);
				if (t >= 1)
					SetSavePointWarpPhase(SavePointWarpPhase.FadeOut);
				break;
			}
			case SavePointWarpPhase.FadeOut:
			{
				var t = Math.Clamp(savePointWarpTime / SavePointWarpFadeOutTime, 0, 1);
				ApplySavePointWarpPose(1, exiting: true);
				EmitSavePointWarpParticles(dt);
				SetSavePointWarpGlitch(SavePointWarpGlitchAmount);
				SavePointWarpFade = Ease.Cube.InOut(t);
				if (t >= 1)
				{
					PerformSavePointWarp();
					SetSavePointWarpPhase(SavePointWarpPhase.Black);
				}
				break;
			}
			case SavePointWarpPhase.Black:
				SavePointWarpFade = 1;
				if (savePointWarpTime >= SavePointWarpBlackTime)
					SetSavePointWarpPhase(SavePointWarpPhase.Enter);
				break;
			case SavePointWarpPhase.Enter:
			{
				var t = Math.Clamp(savePointWarpTime / SavePointWarpEnterTime, 0, 1);
				ApplySavePointWarpPose(t, exiting: false);
				EmitSavePointWarpParticles(dt);
				SetSavePointWarpGlitch((1 - t) * SavePointWarpGlitchAmount);
				SavePointWarpFade = 1 - Ease.Cube.InOut(t);
				if (t >= 1)
					FinishSavePointWarp();
				break;
			}
		}

		View?.UpdateParticles(dt);
		return true;
	}

	void SetSavePointWarpPhase(SavePointWarpPhase phase)
	{
		switch (phase) {
			case SavePointWarpPhase.Enter:
				PlayAudioEvent("teleport_to");
				break;

		}
		savePointWarpPhase = phase;
		savePointWarpTime = 0;
	}

	void PerformSavePointWarp()
	{
		if (GetFirst<Player>() is not { } player)
			return;

		RespawnPlayerAt(player,
			new RespawnState(savePointWarpTarget.RoomId, savePointWarpTarget.Position),
			reloadRoom: room?.Id != savePointWarpTarget.RoomId);
		// The map (and so warp mode) opens during the death animation, so a warp can land here
		// with no health left. State goes back to Normal below, which would otherwise leave a
		// dead player walking around.
		if (player.Health <= 0)
			player.Health = Player.MaxHealth;
		player.State = Player.States.Normal;
		player.Stop();
		EnsureBudForPlayer(repositionExisting: true);
		GetFirst<Bud>()?.Stop();
		easedPlayerCameraTarget = player.Position;
		easedPlayerVelocity = Vector2.Zero;
		cameraLookAhead = Vector2.Zero;
		SnapCameraToGameplayTarget();
		ApplySavePointWarpPose(0, exiting: false);
		ditherGlitchPosition = player.Position;
	}

	void ApplySavePointWarpPose(float t, bool exiting)
	{
		var amount = exiting ? Ease.Cube.In(t) : 1 - Ease.Cube.Out(t);
		if (GetFirst<Player>() is { } player)
			player.Squish = Vector2.Lerp(exiting ? savePointWarpPlayerSquish : Vector2.One, SavePointWarpSquish, amount);
		if (GetFirst<Bud>() is { } bud)
			bud.Squish = Vector2.Lerp(exiting ? savePointWarpBudSquish : Vector2.One, SavePointWarpSquish, amount);
	}

	void EmitSavePointWarpParticles(float dt)
	{
		if (savePointWarpParticleSlot < 0 || Particles is not { Supported: true } particles)
			return;

		savePointWarpParticleRemainder += SavePointWarpParticlesPerSecond * dt;
		var count = (int)savePointWarpParticleRemainder;
		if (count <= 0)
			return;
		savePointWarpParticleRemainder -= count;

		EmitSavePointWarpParticles(particles, savePointWarpParticleSlot, GetFirst<Player>(), "player", count, dt);
		EmitSavePointWarpParticles(particles, savePointWarpParticleSlot, GetFirst<Bud>(), "bud", count, dt);
	}

	static void EmitSavePointWarpParticles(ParticleSystem particles, int slot, Pawn? pawn, string spriteName, int count, float dt)
	{
		if (pawn?.Sprite is not { } sprite || !pawn.Visible)
			return;
		particles.EmitSpriteEdge(slot, spriteName, pawn.AnimationFrameIndex, pawn.Position,
			pawn.Shift - sprite.Origin, pawn.Facing.AsInt, Vector2.Zero, count, dt);
	}

	void SetSavePointWarpGlitch(float amount)
	{
		ditherGlitch = amount;
		ditherGlitchTarget = 0;
		if (GetFirst<Player>() is { } player)
			ditherGlitchPosition = player.Position;
	}

	void FinishSavePointWarp()
	{
		if (GetFirst<Player>() is { } player)
			player.Squish = Vector2.One;
		if (GetFirst<Bud>() is { } bud)
			bud.Squish = Vector2.One;
		savePointWarpPhase = SavePointWarpPhase.None;
		savePointWarpTime = 0;
		SavePointWarpFade = 0;
		ditherGlitch = 0;
		ditherGlitchTarget = 0;
		ditherGlitchPosition = null;
	}
}
