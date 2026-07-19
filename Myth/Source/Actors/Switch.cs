using Foster.Framework;

namespace Myth;

[GameActor(Sprite = "buttons", BoundsX = -8, BoundsY = -8, BoundsW = 16, BoundsH = 16, Tags = ActorTag.System, Anchor = SpriteAnchor.Center)]
public class Switch : PointPathActor
{
	public string TargetActorId = "";
	public float PopSpeed = 80f;
	public float PopSpacing = 8f;

	bool triggered;
	float effectDistance;
	float nextPopDistance;

	public Switch()
	{
		Sprite = Assets.GetSprite("buttons");
		Anchor = SpriteAnchor.Center;
		Hitbox = new(new RectInt(-6, -6, 12, 12));
		Mask = Masks.Destructible;
		IFrameTime = 0;
		Play("top-off");
	}

	public override void OnWasHit(Actor by)
	{
		if (triggered)
			return;

		triggered = true;
		Mask &= ~Masks.Destructible;
		Play("top-on");
		SpawnPop(0);
		nextPopDistance = MathF.Max(1, PopSpacing);
	}

	public override void Update()
	{
		base.Update();
		if (!triggered)
			return;

		var length = PathLength();
		effectDistance = MathF.Min(length, effectDistance + MathF.Max(0, PopSpeed) * Time.Delta);
		while (nextPopDistance <= effectDistance)
		{
			SpawnPop(nextPopDistance);
			nextPopDistance += MathF.Max(1, PopSpacing);
		}

		if (effectDistance < length)
			return;

		if (length > 0 && nextPopDistance - MathF.Max(1, PopSpacing) < length)
			SpawnPop(length);
		if (Game.GetActorById(TargetActorId) is IActivatable target)
			target.Activate(this);
		triggered = false;
	}

	void SpawnPop(float distance) => Game.Create<Pop>((Point2)PointAtDistance(distance));
}
