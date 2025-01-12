﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.Localization;

namespace OhScrap
{
    class EngineFailureModule : BaseFailureModule
    {
        ModuleEngines engine;
        EngineModuleIfc engineWrapper;
        ModuleGimbal gimbal;

        double timeBetweenFailureEvents = 0;

        int fuelLineCounter = 5;

        [KSPField(isPersistant = true, guiActive = false)]
        float fuelFlowMultiplier = 1;

        [KSPField(isPersistant = true, guiActive = false)]
        int spaceEngineExpectedLifetime = 3;
        [KSPField(isPersistant = true, guiActive = false)]
        float spaceEngineBaseChanceOfFailure = 0.1f;


        protected override void Overrides()
        {
            Fields["displayChance"].guiName = Localizer.Format("#OHS-eng-00");
            Fields["safetyRating"].guiName = Localizer.Format("#OHS-eng-01");

            engine = part.FindModuleImplementing<ModuleEngines>();
            engineWrapper = EngineModuleWrapper.getInstance(part, engine.engineID);

            gimbal = part.FindModuleImplementing<ModuleGimbal>();
            //If the ISP at sea level suggests this is a space engine, change the lifetime and failure rates accordingly
            float staticPressure = (float)(FlightGlobals.GetHomeBody().GetPressure(0) * PhysicsGlobals.KpaToAtmospheres);
            if (engine.atmosphereCurve.Evaluate(staticPressure) <= 100.0f)
            {
                expectedLifetime = spaceEngineExpectedLifetime;
                baseChanceOfFailure = spaceEngineBaseChanceOfFailure;
            }
        }

        public override bool FailureAllowed()
        {
            if (engine == null) return false;
            if (engine.currentThrottle == 0) return false;
            return HighLogic.CurrentGame.Parameters.CustomParams<Settings>().EngineFailureModuleAllowed;
        }

        public override void FailPart()
        {
            if (!engine) return;
            //In the event of a fuel line leak, the chance of explosion will be reset if the engine is shut down.
            if (engine.currentThrottle == 0)
            {
                fuelLineCounter = 5;
                return;
            }

            if (OhScrap.highlight) OhScrap.SetFailedHighlight();
            //Randomly pick which failure we will give the player
            if (failureType == "none")
            {
                int i = Utils.instance._randomiser.Next(1, 7);
                switch (i)
                {
                    case 1:
                        // failureType = Localizer.Format("#OHS-eng-03");
                        failureType = "Fuel Flow Failure";
                        Debug.Log("[OhScrap]: attempted to perform Fuel Flow Failure on " + SYP.ID);
                        break;
                    case 2:
                        //failureType = Localizer.Format("#OHS-eng-04");
                        failureType = "Fuel Line Leak";
                        Debug.Log("[OhScrap]: attempted to perform Fuel Line Leak on " + SYP.ID);
                        InvokeRepeating("LeakFuel", 2.0f, 2.0f);
                        break;
                    case 3:
                        //failureType = Localizer.Format("#OHS-eng-05");
                        failureType = "Underthrust";
                        Debug.Log("[OhScrap]: attempted to perform Underthrust on " + SYP.ID);
                        break;
                    case 4:
                        if (gimbal == null) return;
                        //failureType = Localizer.Format("#OHS-eng-06");
                        failureType = "Gimbal Failure";
                        Debug.Log("[OhScrap]: attempted to lock gimbal on " + SYP.ID);
                        break;
                    case 5:
                        //failureType = Localizer.Format("#OHS-eng-07");
                        failureType = "Stable Underthrust";
                        Debug.Log("[OhScrap]: attempted to perform Stable Underthrust on " + SYP.ID);
                        break;
                    case 6:
                        //failureType = Localizer.Format("#OHS-eng-08");
                        failureType = "Performance Loss";
                        Debug.Log("[OhScrap]: attempted to perform Performance Loss on " + SYP.ID);
                        break;
                    default:
                        //failureType = Localizer.Format("#autoLOC_258911");
                        failureType = "none";
                        Debug.Log("[OhScrap]: " + SYP.ID + " decided not to fail after all");
                        break;
                }
                return;
            }
            switch (failureType)
            {
                //Engine shutdown
                //case Localizer.Format("#OHS-eng-04"):
                case "Fuel Flow Failure":
                    engine.Shutdown();
                    break;
                //Fuel line leaks will explode the engine after anywhere between 5 and 50 seconds.
                case "Fuel Line Leak":
                    if (timeBetweenFailureEvents > Planetarium.GetUniversalTime()) break;
                    if (fuelLineCounter < 0) part.explode();
                    else fuelLineCounter--;
                    timeBetweenFailureEvents = Planetarium.GetUniversalTime() + Utils.instance._randomiser.Next(1, 5);
                    break;
                //Engine will constantly lose thrust
                case "Underthrust":
                    if (timeBetweenFailureEvents <= Planetarium.GetUniversalTime())
                    {
                        fuelFlowMultiplier *= 0.9f;
                        timeBetweenFailureEvents = Planetarium.GetUniversalTime() + Utils.instance._randomiser.Next(10, 30);
                    }
                    engineWrapper.SetFuelFlowMult(fuelFlowMultiplier);
                    break;
                //lock gimbal
                case "Gimbal Failure":
                    gimbal.gimbalLock = true;
                    break;
                // Engine permanently has 50% max thrust
                case "Stable Underthrust":
                    engineWrapper.SetFuelFlowMult(0.5f);
                    break;
                // Isp and thrust both set to 50% of normal
                case "Performance Loss":
                    engineWrapper.SetFuelIspMult(0.5f);
                    break;
                default:
                    return;
            }
            //PlaySound();
        }

        public override void RepairPart()
        {
            switch (failureType)
            {
                case "Fuel Flow Failure":
                    engine.Activate();
                    Debug.Log("[OhScrap]: Re-activated " + SYP.ID);
                    break;
                case "Underthrust":
                case "Stable Underthrust":
                    fuelFlowMultiplier = 1f;
                    engineWrapper.SetFuelFlowMult(1f);
                    Debug.Log("[OhScrap]: Reset Thrust on " + SYP.ID);
                    break;
                case "Gimbal Failure":
                    gimbal.gimbalLock = false;
                    break;
                case "Fuel Line Leak":
                    CancelInvoke("LeakFuel");
                    break;
                case "Performance Loss":
                    engineWrapper.SetFuelIspMult(1f);
                    Debug.Log("[OhScrap]: Reset Isp on " + SYP.ID);
                    break;
                default:
                    return;
            }
        }

        void LeakFuel()
        {
            part.RequestResource("LiquidFuel", 1.0);
            ScreenMessages.PostScreenMessage("Fuel Line Leaking!");
        }
    }

}
