using System.Numerics;
using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "buttons", BoundsX = -8, BoundsY = -8, BoundsW = 16, BoundsH = 16, Tags = ActorTag.System,
	Anchor = SpriteAnchor.Center, LivePositionEditable = true)]
public class Switch : PointPathActor, IActivationSource
{
	[ActorRef(typeof(IActivatable))] public string TargetActorId = "";
	public bool Persistent;
	[I(20, 240)] public float PulseSpeed = 85f;
	[I(8, 192)] public float PulseLength = 64f;

	bool triggered;
	bool targetActive;
	float effectDistance;
	LightningEffectPoint[] pulsePoints = [];
	Light? pulseLight;

	public Switch()
	{
		Sprite = Assets.GetSprite("buttons");
		Anchor = SpriteAnchor.Center;
		Hitbox = new(new RectInt(-6, -6, 12, 12));
		Mask = Masks.Destructible;
		IFrameTime = 0;
		Play("top-off");
	}

	public override void Added()
	{
		base.Added();
		if (!PersistentlyTriggered)
			return;
		Mask &= ~Masks.Destructible;
		Play("top-on");
	}

	bool PersistentlyTriggered => Persistent && Game.IsButtonPressed(ActorId);
	bool IActivationSource.ActivationIsPersistent => Persistent;

	internal override void OnWasHit(HitInfo hitInfo)
	{
		if (triggered || PersistentlyTriggered)
			return;

		triggered = true;
		Mask &= ~Masks.Destructible;
		Play("top-on");
		effectDistance = 0;
		pulseLight = ActorSpecs.Create(Game, "SwitchPulseLight", (Point2)PointAtDistance(0)) as Light;
		pulseLight?.SpriteShadowOwner = this;
	}

	public override void Update()
	{
		base.Update();
		if (PersistentlyTriggered)
		{
			if (!targetActive)
				ActivateTarget();
			return;
		}
		if (!triggered)
			return;

		var length = PathLength();
		effectDistance = MathF.Min(length, effectDistance + MathF.Max(0, PulseSpeed) * Time.Delta);
		pulseLight?.Position = (Point2)PointAtDistance(effectDistance);

		if (effectDistance < length)
			return;

		if (Persistent)
			Game.PressButtonPersistently(ActorId);
		ActivateTarget();
		triggered = false;
		DestroyPulseLight();
	}

	void ActivateTarget()
	{
		if (Game.GetActorById(TargetActorId) is not IActivatable target)
			return;
		target.SetActive(this, true);
		targetActive = true;
	}

	public override void Destroyed() => DestroyPulseLight();

	public override void Render(Batcher batcher)
	{
		base.Render(batcher);
		if (!triggered || effectDistance <= 0 || Points.Length < 2)
			return;

		var count = BuildPulsePoints();
		LightningEffect.Render(batcher, pulsePoints.AsSpan(0, count), Time.SecondsF,
			Position.X * 397 ^ Position.Y);
	}

	int BuildPulsePoints()
	{
		if (pulsePoints.Length < Points.Length + 2)
			pulsePoints = new LightningEffectPoint[Points.Length + 2];

		var tail = MathF.Max(0, effectDistance - MathF.Max(1, PulseLength));
		var distance = 0f;
		var count = AddPulsePoint(0, PointAtDistance(tail), 0);
		for (var i = 1; i < Points.Length; i++)
		{
			distance += Vector2.Distance(Points[i - 1], Points[i]);
			if (distance > tail && distance < effectDistance)
				count = AddPulsePoint(count, WorldPoint(i), (distance - tail) / MathF.Max(1, PulseLength));
		}
		return AddPulsePoint(count, PointAtDistance(effectDistance), 1);
	}

	int AddPulsePoint(int count, Vector2 worldPoint, float intensity)
	{
		var point = (Point2)(worldPoint - Position);
		if (count > 0 && pulsePoints[count - 1].Position == point)
		{
			pulsePoints[count - 1] = new(point, intensity);
			return count;
		}

		pulsePoints[count] = new(point, intensity);
		return count + 1;
	}

	void DestroyPulseLight()
	{
		if (pulseLight == null)
			return;
		Game.Destroy(pulseLight);
		pulseLight = null;
	}
}
