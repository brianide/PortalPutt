using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OTAPI;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Golf;
using Terraria.ID;
using Terraria.Physics;
using TerrariaApi.Server;
using TShockAPI;

namespace PortalPutt
{
	[ApiVersion(2, 1)]
	public class PortalPuttPlugin : TerrariaPlugin
	{
		public override string Name => "Portal Putt";
		public override string Description => "Implements missing crucial functionality";
		public override string Author => "gigabarn";
		public override Version Version => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
		public PortalPuttPlugin(Main game) : base(game) { }

		// Wrap vanilla collision listener
		private readonly static PortalListener listener = new PortalListener(GolfHelper.Listener);

		public override void Initialize()
		{
			ServerApi.Hooks.ProjectileAIUpdate.Register(this, args =>
			{
				var proj = args.Projectile;
				if (!ProjectileID.Sets.IsAGolfBall[proj.type])
					return;

				// Override vanilla golfball physics
				args.Handled = true;

				// Keep the ball alive
				if (!proj.npcProj)
					proj.timeLeft = Math.Max(proj.timeLeft, 10);

				// Become active if the tee we were sitting on was displaced
				if (proj.ai[1] == -1f)
				{
					var tileSafely = Framing.GetTileSafely(proj.Bottom.ToTileCoordinates());
					if (!tileSafely.active() || tileSafely.type != TileID.GolfTee)
					{
						proj.ai[1] = 0;
						proj.netUpdate = true;
					}

					return;
				}

				// Do a physics step with our listener attached
				listener.NeedNetSync = false;
				var ballState = BallCollision.Step(GolfHelper.PhysicsProperties, proj, ref proj.localAI[0], listener).State;
				switch (ballState)
				{
					case BallState.Moving:
						if (Main.player.IndexInRange(proj.owner) && Main.player[proj.owner].active)
							RemoteClient.CheckSection(proj.owner, proj.position);
						break;

					case BallState.Resting:
						proj.damage = 0;
						break;
				}

				// Update the client
				if (listener.NeedNetSync)
					TSPlayer.All.SendData(PacketTypes.ProjectileNew, number: proj.whoAmI);

				// Store the ball state
				proj.localAI[1] = (float)ballState;
			});
		}
	}

	class PortalListener : IBallContactListener
	{
		private readonly IBallContactListener wrapped;
		public bool NeedNetSync { get; set; }
		public PortalListener(IBallContactListener wrapped) => this.wrapped = wrapped;

		// Don't care about passthroughs, so just forward them
		public void OnPassThrough(PhysicsProperties properties, ref Vector2 position, ref Vector2 velocity, ref float angularVelocity, ref BallPassThroughEvent passThrough) =>
			wrapped.OnPassThrough(properties, ref position, ref velocity, ref angularVelocity, ref passThrough);

		// Run a portal pass when we collide with something
		public void OnCollision(PhysicsProperties properties, ref Vector2 position, ref Vector2 velocity, ref BallCollisionEvent collision)
		{
			if (TryPortal(ref position, ref velocity, ref collision))
				NeedNetSync = true;
			else
				wrapped.OnCollision(properties, ref position, ref velocity, ref collision);
		}

		private bool TryPortal(ref Vector2 position, ref Vector2 velocity, ref BallCollisionEvent collision)
		{
			var ent = collision.Entity;

			foreach (var (pin, pout) in Util.GetPortalPairs())
			{
				PortalHelper.GetPortalEdges(pin.Center, pin.ai[0], out Vector2 start, out Vector2 end);
				if (Collision.CheckAABBvLineCollision(collision.ImpactPoint - ent.Size / 2, ent.Size, start, end))
				{
					// The velocity vector has already been reflected from the collision we're responding to, so
					// we reflect it back. We then rotate the velocity vector by the rotation of the exit portal less the
					// rotation of the entry portal. The vector is backwards at that point, so we negate it.
					velocity = Vector2.Reflect(velocity, collision.Normal).RotatedBy(pout.ai[0] - pin.ai[0]) * -1;

					// We subtract the entry portal's position from the impact point, then rotate the resulting vector to
					// be axis-aligned. We flip the X, which is hard to explain without a whiteboard, and then set the Y
					// so that the ball is sure to not be clipping with the surface the exit portal is on. Then we rotate
					// and translate onto the exit portal.
					var portalRelative = (position - pin.Center).RotatedBy(-pin.ai[0]);
					portalRelative.X *= -1;
					portalRelative.Y = Math.Max(portalRelative.Y, ent.Size.Y * 0.6f);
					position = portalRelative.RotatedBy(pout.ai[0]) + pout.Center;

					return true;
				}
			}

			return false;
		}
	}
}
