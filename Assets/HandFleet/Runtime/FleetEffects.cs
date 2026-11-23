using UnityEngine;
namespace HandFleet
{
    // All effect ages use the simulation clock. Pools are allocated only during setup.
    public sealed class FleetEffects : MonoBehaviour
    {
        struct Particle { public Transform View;public Renderer Renderer;public Vector3 Position,Velocity;public float Age,Life,Size; }
        Particle[] particles=new Particle[72];AudioSource[] voices=new AudioSource[16];int[] priorities=new int[16];
        AudioSource ambience;FleetArtAssets art;Material cyan,amber,lime,ivory;float volume=.35f,lastClock,lastNear=-10;bool paused,reduced;
        AudioSource jetEngine, followerEngine, heavyEngine, finalHullImpact;
        float nozzleEffort, engineLoad;
        LineRenderer laser,shield;LineRenderer[] beams;KestrelExhaust exhaust;FleetMotor motor;int cursor;
        public void Build(HandFleetRun run,FleetArtAssets library,Material player,Material threat,Material pickup,Material armor)
        {
            art=library;cyan=player;amber=threat;lime=pickup;ivory=armor;
            for(int i=0;i<particles.Length;i++){var t=FleetGeometry.Shape(transform,"Effect pool "+i,PrimitiveType.Cube,Vector3.zero,Vector3.one*.01f,cyan);particles[i]=new Particle{View=t,Renderer=t.GetComponent<Renderer>()};t.gameObject.SetActive(false);}
            for(int i=0;i<voices.Length;i++) voices[i]=SpatialVoice("Spatial voice "+i);
            if (art && art.JetEngineLoop) jetEngine = EngineVoice("Kestrel engine", art.JetEngineLoop);
            if (art && art.FollowerEngineLoop) followerEngine = EngineVoice("Nearest Follower engine", art.FollowerEngineLoop);
            if (art && art.HeavyEngineLoop) heavyEngine = EngineVoice("Heavy ship engine", art.HeavyEngineLoop);
            ambience=gameObject.AddComponent<AudioSource>();ambience.playOnAwake=false;ambience.loop=true;ambience.spatialBlend=0;if(art&&art.Ambience){ambience.clip=art.Ambience;ambience.Play();}
            laser=FleetGeometry.Line(transform,"Charged laser",cyan,.04f,2);laser.gameObject.SetActive(false);
            shield=FleetGeometry.Line(transform,"Shield protection",cyan,.003f,36,true);FleetGeometry.Ring(shield,.055f);shield.gameObject.SetActive(false);
            beams=new LineRenderer[run.Enemies.Length];for(int i=0;i<beams.Length;i++){beams[i]=FleetGeometry.Line(transform,"Enemy beam and aim warning",amber,.003f,2);beams[i].gameObject.SetActive(false);}
            motor=run.Motor;
            exhaust=new KestrelExhaust(transform,cyan);
            motor.NozzleCommand+=ApplyNozzles;

        }
        AudioSource SpatialVoice(string label)
        {
            var source = FleetGeometry.Root(transform,label).gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1;
            source.minDistance = .8f;
            source.maxDistance = 9;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0;
            return source;
        }
        AudioSource EngineVoice(string label, AudioClip clip)
        {
            var source = SpatialVoice(label);
            source.clip = clip;
            source.loop = true;
            source.priority = 150;
            return source;
        }
        void ApplyNozzles(Vector3 offset, Vector3 turn)
        {
            exhaust.Apply(offset,turn);
            nozzleEffort = Mathf.Clamp01(Mathf.Max(offset.magnitude / .055f, turn.magnitude / 30f));
        }
        void PresentEngines(HandFleetRun run, float dt, float gain)
        {
            if (run.State != RunState.Running)
            {
                if (jetEngine && jetEngine.isPlaying) jetEngine.Stop();
                if (followerEngine && followerEngine.isPlaying) followerEngine.Stop();
                if (heavyEngine && heavyEngine.isPlaying) heavyEngine.Stop();
                engineLoad = nozzleEffort = 0;
                return;
            }
            if (jetEngine)
            {
                var effort = Mathf.Max(nozzleEffort, Mathf.Clamp01(run.Motor.Velocity.magnitude));
                engineLoad = Mathf.MoveTowards(engineLoad, effort, dt * 6f);
                jetEngine.transform.localPosition = run.Motor.Position;
                jetEngine.volume = gain * Mathf.Lerp(.12f, .48f, engineLoad);
                jetEngine.pitch = Mathf.Lerp(.88f, 1.38f, engineLoad);
                if (!jetEngine.isPlaying) jetEngine.Play();
            }
            if (!followerEngine && !heavyEngine) return;
            int follower = -1, heavy = -1;
            float followerDistance = float.PositiveInfinity, heavyDistance = float.PositiveInfinity;
            for (int i = 0; i < run.Enemies.Length; i++)
            {
                var enemy = run.Enemies[i];
                if (!enemy.Active) continue;
                float distance = (enemy.Position - run.Motor.Position).sqrMagnitude;
                if (enemy.Family == EnemyFamily.Follower && enemy.State != EnemyState.Arriving && distance < followerDistance)
                {
                    follower = i;
                    followerDistance = distance;
                }
                else if (enemy.Family is EnemyFamily.Frigate or EnemyFamily.Battleship or EnemyFamily.Carrier or EnemyFamily.Gatekeeper)
                {
                    if (distance >= heavyDistance) continue;
                    heavy = i;
                    heavyDistance = distance;
                }
            }
            float proximity = 1f - Mathf.Clamp01(Mathf.Sqrt(followerDistance) / 1.5f);
            PlayEnemyEngine(followerEngine, run, follower, gain * Mathf.Lerp(.18f, .6f, proximity), Mathf.Lerp(1.25f, 1.8f, proximity));
            PlayEnemyEngine(heavyEngine, run, heavy, gain * .25f, 1f);
        }
        static void PlayEnemyEngine(AudioSource voice, HandFleetRun run, int index, float gain, float pitch)
        {
            if (!voice) return;
            if (index < 0)
            {
                if (voice.isPlaying) voice.Stop();
                return;
            }
            voice.transform.localPosition = run.Enemies[index].Position;
            voice.volume = gain;
            voice.pitch = pitch;
            if (!voice.isPlaying) voice.Play();
        }
        public void Cue(FleetCombatEvent cue,HandFleetRun run)
        {
            if (cue.Kind == FleetCue.Start && finalHullImpact)
            {
                finalHullImpact.Stop();
                finalHullImpact = null;
            }
            if(cue.Kind==FleetCue.NearMiss){if(run.Clock-lastNear<.3f)return;lastNear=run.Clock;}
            int priority = cue.Kind switch
            {
                FleetCue.PlayerShot => 0,
                FleetCue.EnemyShot or FleetCue.EnemyHit or FleetCue.WarpArrival or FleetCue.CarrierLaunch => 1,
                _ => 3
            };
            float pitch = 1f;
            var clip = art ? art.SoundFor(cue, out pitch) : null;
            if(clip)
            {
                int slot=-1;for(int i=0;i<voices.Length;i++)if(!voices[i].isPlaying){slot=i;break;}
                if(slot<0)for(int i=0;i<voices.Length;i++)if(priorities[i]<priority){slot=i;break;}
                if(slot>=0)
                {
                    var voice = voices[slot];
                    var position = cue.Position;
                    if (run.ArcadeRules && cue.Kind == FleetCue.PlayerShot)
                    {
                        position += cue.Direction * HandFleetRun.GunMuzzle;
                        pitch *= 1f - .06f * run.WeaponLevel;
                    }
                    voice.Stop();
                    voice.transform.localPosition = position;
                    voice.clip = clip;
                    voice.pitch = pitch;
                    voice.volume = volume * (priority == 0 ? .12f : priority == 1 ? .35f : .75f);
                    voice.priority = priority == 3 ? 32 : priority == 1 ? 100 : 180;
                    priorities[slot] = priority;
                    if (cue.Kind == FleetCue.Hit && run.Shields == 0) finalHullImpact = voice;
                    else if (voice == finalHullImpact) finalHullImpact = null;
                    voice.Play();
                }
            }
            int count=cue.Kind==FleetCue.Kill?5:cue.Kind==FleetCue.Hit||cue.Kind==FleetCue.ShieldBreak?8:cue.Kind==FleetCue.Fusion?12:cue.Kind==FleetCue.EnemyHit?3:cue.Kind==FleetCue.Absorb?3:cue.Kind==FleetCue.Pickup?8:cue.Kind==FleetCue.PlayerShot?1:cue.Kind==FleetCue.BossPhase?20:0;
            if(reduced)count=(count+2)/3;
            var material=cue.Kind==FleetCue.Pickup?lime:cue.Kind==FleetCue.EnemyHit?amber:cue.Kind==FleetCue.Kill?ivory:cyan;
            for(int i=0;i<count;i++)
            {
                int slot=cursor++%particles.Length;var p=particles[slot];float a=(i*2.39996f+slot)*1.1f;
                p.Position=cue.Position+(cue.Kind==FleetCue.PlayerShot?cue.Direction*.15f:Vector3.zero);p.Age=0;p.Life=cue.Kind==FleetCue.PlayerShot?.06f:.35f;
                p.Velocity=cue.Kind==FleetCue.PlayerShot?cue.Direction*.5f:new Vector3(Mathf.Cos(a),Mathf.Sin(a),Mathf.Sin(a*1.7f))*.45f;
                p.Size=cue.Kind==FleetCue.PlayerShot?.012f:.012f*cue.Intensity;p.Renderer.sharedMaterial=material;p.View.gameObject.SetActive(true);particles[slot]=p;
            }
        }
        public void Present(HandFleetRun run,bool reduce,float gain)
        {
            reduced=reduce;volume=gain;float dt=Mathf.Max(0,run.Clock-lastClock);
            bool results = run.State == RunState.Results;
            if(run.Clock<lastClock || results || run.State==RunState.Calibration)
            {
                lastNear=-10;cursor=0;
                for(int i=0;i<particles.Length;i++){particles[i].Life=0;particles[i].View.gameObject.SetActive(false);}
                foreach(var voice in voices)
                    if (!results || voice != finalHullImpact) voice.Stop();
            }
            lastClock=run.Clock;
            bool hold=run.State!=RunState.Running;
            if(hold!=paused)
            {
                foreach(var voice in voices)
                {
                    if (hold && !(results && voice == finalHullImpact)) voice.Pause();
                    else if (!hold) voice.UnPause();
                }
                if(hold)ambience.Pause();else ambience.UnPause();
                paused=hold;
            }
            ambience.volume=gain*.3f;
            PresentEngines(run,dt,gain);
            for(int i=0;i<particles.Length;i++)
            {
                var p=particles[i];if(p.Life<=0)continue;p.Age+=dt;if(p.Age>=p.Life){p.Life=0;p.View.gameObject.SetActive(false);}else{p.Position+=p.Velocity*dt;p.View.localPosition=p.Position;p.View.localScale=Vector3.one*p.Size*(1-p.Age/p.Life);p.View.localRotation*=Quaternion.Euler(dt*120,dt*80,dt*60);}particles[i]=p;
            }
            laser.gameObject.SetActive(run.LaserRemaining>0);laser.startWidth=laser.endWidth=run.Settings.LaserRadius*2;laser.SetPosition(0,run.Motor.Position);laser.SetPosition(1,run.Motor.Position+run.Motor.Rotation*Vector3.forward*run.Settings.LaserRange);
            shield.gameObject.SetActive(run.ShieldActive||(run.Clock<run.InvulnerableUntil&&run.State!=RunState.Results));shield.transform.localScale=Vector3.one*(run.ShieldActive? .12f/.055f:1);shield.transform.localPosition=run.Motor.Position;shield.transform.localRotation=run.Motor.Rotation;
            for(int i=0;i<beams.Length;i++)
            {
                var e=run.Enemies[i];var b=beams[i];bool sight=e.Family==EnemyFamily.Sniper&&e.State==EnemyState.Telegraph&&e.Timer<=.12f;bool visible=e.Active&&(sight||e.Family==EnemyFamily.Lancer&&(e.State==EnemyState.Telegraph||e.Beam));b.gameObject.SetActive(visible);
                if(!visible)continue;b.startWidth=b.endWidth=e.Beam?run.Settings.EnemyBeamRadius*2:.002f;b.SetPosition(0,e.Position);b.SetPosition(1,sight?run.Motor.Position:e.Position+(e.Aim-e.Position).normalized*run.Settings.ArenaRadius*2);
            }
            exhaust.Reduced=reduce;
            if(hold) exhaust.Apply(Vector3.zero,Vector3.zero);
        }
        // Settings preview: its own voice, because flight voices are stopped every frame while the ship is parked.
        AudioSource preview;
        public void PreviewVolume(float gain)
        {
            if(!art||art.Sounds==null||(int)FleetCue.Pickup>=art.Sounds.Length||!art.Sounds[(int)FleetCue.Pickup])return;
            if(!preview){preview=gameObject.AddComponent<AudioSource>();preview.playOnAwake=false;preview.spatialBlend=0;}
            preview.Stop();preview.clip=art.Sounds[(int)FleetCue.Pickup];preview.volume=gain*.75f;preview.Play();
        }
        void OnDestroy()
        {
            if(motor!=null) motor.NozzleCommand-=ApplyNozzles;
        }
    }
}
