﻿using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Steering : Powered
    {
        private const float AutopilotRayCastInterval = 5.0f;

        private Vector2 currVelocity;
        private Vector2 targetVelocity;

        private GUITickBox autopilotTickBox, maintainPosTickBox;

        private bool autoPilot;

        private Vector2? posToMaintain;

        private SteeringPath steeringPath;

        private PathFinder pathFinder;

        private float networkUpdateTimer;
        private bool valueChanged;

        private float autopilotRayCastTimer;

        private float neutralBallastLevel;
        
        bool AutoPilot
        {
            get { return autoPilot; }
            set
            {
                if (value == autoPilot) return;

                autoPilot = value;

                autopilotTickBox.Selected = value;

                maintainPosTickBox.Enabled = autoPilot;
                
                if (autoPilot)
                {
                    if (pathFinder==null) pathFinder = new PathFinder(WayPoint.WayPointList, false);
                    steeringPath = pathFinder.FindPath(
                        ConvertUnits.ToSimUnits(item.WorldPosition),
                        ConvertUnits.ToSimUnits(Level.Loaded.EndPosition));
                }
                else
                {
                    maintainPosTickBox.Selected = false;
                    posToMaintain = null;
                }
            }
        }


        [Editable, HasDefaultValue(0.5f, true)]
        public float NeutralBallastLevel
        {
            get { return neutralBallastLevel; }
            set
            {
                neutralBallastLevel = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        private Vector2 TargetVelocity
        {
            get { return targetVelocity;}
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f);
            }
        }

        public Vector2 CurrTargetVelocity
        {
            get { return targetVelocity; }
        }

        public SteeringPath SteeringPath
        {
            get { return steeringPath; }
        }

        public Steering(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            autopilotTickBox = new GUITickBox(new Rectangle(0,25,20,20), "Autopilot", Alignment.TopLeft, GuiFrame);
            autopilotTickBox.OnSelected = (GUITickBox box) =>
            {
                AutoPilot = box.Selected;
                valueChanged = true;

                return true;
            };

            maintainPosTickBox = new GUITickBox(new Rectangle(0, 50, 20, 20), "Maintain position", Alignment.TopLeft, GuiFrame);
            maintainPosTickBox.Enabled = false;
            maintainPosTickBox.OnSelected = ToggleMaintainPosition;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            //base.Update(deltaTime, cam);

            if (valueChanged)
            {
                networkUpdateTimer -= deltaTime;
                if (networkUpdateTimer <= 0.0f)
                {
                    item.NewComponentEvent(this, true, false);
                    networkUpdateTimer = 0.5f;
                    valueChanged = false;
                }
            }
     
            if (voltage < minVoltage) return;
               
            if (autoPilot)
            {
                UpdateAutoPilot(deltaTime);
            }

            item.SendSignal(0, targetVelocity.X.ToString(CultureInfo.InvariantCulture), "velocity_x_out");

            float targetLevel = -targetVelocity.Y;

            targetLevel += (neutralBallastLevel - 0.5f) * 100.0f;

            item.SendSignal(0, targetLevel.ToString(CultureInfo.InvariantCulture), "velocity_y_out");


            voltage -= deltaTime;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //if (voltage < minVoltage) return;

            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Update(1.0f / 60.0f);
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage) return;

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);
            //GUI.DrawRectangle(spriteBatch, velRect, Color.White, false);

            if (Submarine.Loaded != null && Level.Loaded != null)
            {
                Vector2 realWorldVelocity = ConvertUnits.ToDisplayUnits(Submarine.Loaded.Velocity * Physics.DisplayToRealWorldRatio) * 3.6f;
                float realWorldDepth = Math.Abs(Submarine.Loaded.Position.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 65), 
                    "Velocity: " + (int)realWorldVelocity.X + " km/h", Color.LightGreen, null, 0, GUI.SmallFont);
                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 50), 
                    "Descent velocity: " + -(int)realWorldVelocity.Y + " km/h", Color.LightGreen, null, 0, GUI.SmallFont);

                GUI.DrawString(spriteBatch, new Vector2(x + 20, y + height - 30),
                    "Depth: " + (int)realWorldDepth + " m", Color.LightGreen, null, 0, GUI.SmallFont);
            }

            

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X,velRect.Center.Y), 
                new Vector2(velRect.Center.X + currVelocity.X, velRect.Center.Y - currVelocity.Y), 
                Color.Gray);

            Vector2 targetVelPos = new Vector2(velRect.Center.X + targetVelocity.X, velRect.Center.Y - targetVelocity.Y);

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                targetVelPos,
                Color.LightGray);

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X - 5, (int)targetVelPos.Y - 5, 10, 10), Color.White);

            if (Vector2.Distance(PlayerInput.MousePosition, new Vector2(velRect.Center.X, velRect.Center.Y)) < 200.0f)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)targetVelPos.X -10, (int)targetVelPos.Y - 10, 20, 20), Color.Red);

                if (PlayerInput.LeftButtonHeld())
                {
                    TargetVelocity = PlayerInput.MousePosition - new Vector2(velRect.Center.X, velRect.Center.Y);
                    targetVelocity.Y = -targetVelocity.Y;

                    valueChanged = true;
                }
            }
        }

        private void UpdateAutoPilot(float deltaTime)
        {
            if (posToMaintain==null)
            {

                autopilotRayCastTimer -= deltaTime;

                steeringPath.CheckProgress(ConvertUnits.ToSimUnits(item.WorldPosition), 10.0f);

                if (autopilotRayCastTimer<=0.0f && steeringPath.NextNode != null)
                {
                    Vector2 diff = Vector2.Normalize(ConvertUnits.ToSimUnits(steeringPath.NextNode.Position - Submarine.Loaded.WorldPosition));

                    bool nextVisible = true;
                    for (int x = -1; x < 2; x += 2)
                    {
                        for (int y = -1; y < 2; y += 2)
                        {
                            Vector2 cornerPos =
                                new Vector2(Submarine.Borders.Width * x, Submarine.Borders.Height * y) / 2.0f;

                            cornerPos = ConvertUnits.ToSimUnits(cornerPos * 1.2f + Submarine.Loaded.WorldPosition);

                            float dist = Vector2.Distance(cornerPos, steeringPath.NextNode.SimPosition);

                            if (Submarine.PickBody(cornerPos, cornerPos + diff*dist, null, Physics.CollisionLevel) == null) continue;

                            nextVisible = false;
                            x = 2;
                            y = 2;
                        }
                    }

                    if (nextVisible) steeringPath.SkipToNextNode();

                    autopilotRayCastTimer = AutopilotRayCastInterval;                
                }

                if (steeringPath.CurrentNode != null)
                {
                    SteerTowardsPosition(steeringPath.CurrentNode.WorldPosition);
                }
            }
            else
            {
                SteerTowardsPosition((Vector2)posToMaintain);
            }

        }

        private void SteerTowardsPosition(Vector2 worldPosition)
        {
            float prediction = 10.0f;

            Vector2 futurePosition = ConvertUnits.ToDisplayUnits(item.Submarine.Velocity) * prediction;
            Vector2 targetSpeed = ((worldPosition - item.WorldPosition) - futurePosition);

            if (targetSpeed.Length()>500.0f)
            {
                targetSpeed = Vector2.Normalize(targetSpeed);
                TargetVelocity = targetSpeed * 100.0f;
            }
            else
            {
                targetVelocity = targetSpeed/5.0f;
            }


        }

        private bool ToggleMaintainPosition(GUITickBox tickBox)
        {
            valueChanged = true;

            if (tickBox.Selected)
            {
                if (Submarine.Loaded == null)
                {
                    posToMaintain = null;
                }
                else
                {
                    posToMaintain = item.WorldPosition;
                }
            }
            else
            {
                posToMaintain = null;
            }

            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (connection.Name == "velocity_in")
            {
                currVelocity = ToolBox.ParseToVector2(signal, false);
            }
            else
            {
                base.ReceiveSignal(stepsTaken, signal, connection, sender, power);
            }
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.Write(targetVelocity.X);
            message.Write(targetVelocity.Y);

            message.Write(autoPilot);
            if (autoPilot)
            {
                message.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    message.Write(((Vector2)posToMaintain).X);
                    message.Write(((Vector2)posToMaintain).Y);
                }
            }

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            Vector2 newTargetVelocity   = Vector2.Zero;
            bool newAutoPilot           = false;

            Vector2? newPosToMaintain = null;

            try
            {
                newTargetVelocity = new Vector2(message.ReadFloat(), message.ReadFloat());
                newAutoPilot = message.ReadBoolean();
                if (newAutoPilot)
                {
                    bool maintainPos = message.ReadBoolean();
                    if (maintainPos)
                    {
                        newPosToMaintain = new Vector2(
                            message.ReadFloat(), 
                            message.ReadFloat());
                    }
                }
            }

            catch
            {
                return;
            }

            TargetVelocity = newTargetVelocity;
            AutoPilot = newAutoPilot;

            maintainPosTickBox.Selected = newPosToMaintain != null;
            posToMaintain = newPosToMaintain;
        }
    }
}
