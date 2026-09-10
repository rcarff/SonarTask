using UnityEngine;
using UnityEngine.UI;

namespace SonarTask.UI
{
    /// <summary>
    /// Creates the editable, scene-resident parts of each screen. Runtime code binds to
    /// these named objects and only creates experiment-dependent content dynamically.
    /// </summary>
    public static class SceneLayoutBuilder
    {
        public const string RootName = "SceneUIRoot";

        public static RectTransform Ensure(string sceneName, Transform canvas)
        {
            var existing = canvas.Find(RootName) as RectTransform;
            if (existing) return existing;
            var root = UIFactory.Rect(RootName, canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            switch (sceneName)
            {
                case "LoginWeb": BuildLogin(root); break;
                case "Startup": BuildStartup(root); break;
                case "ExperimentSelection": BuildSelection(root); break;
                case "Instructions": BuildInstructions(root); break;
                case "SettingsWeb": BuildSettings(root); break;
                case "SonarTask": BuildSonarTask(root); break;
            }
            return root;
        }

        public static RectTransform Rebuild(string sceneName, Transform canvas)
        {
            var old = canvas.Find(RootName);
            if (old)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) Object.DestroyImmediate(old.gameObject);
                else Object.Destroy(old.gameObject);
#else
                Object.Destroy(old.gameObject);
#endif
            }
            return Ensure(sceneName, canvas);
        }

        static void BuildLogin(RectTransform root)
        {
            var panel = UIFactory.Rect("LoginPanel", root, new Vector2(.32f,.25f), new Vector2(.68f,.75f), Vector2.zero, Vector2.zero);
            AddBackground(panel);
            UIFactory.VLayout(panel,14,28);
            var title=UIFactory.Text("SONAR Simulator Task",panel,32,TextAnchor.MiddleCenter); title.gameObject.name="Title"; UIFactory.Size(title,70);
            var sub=UIFactory.Text("Operator Login",panel,22,TextAnchor.MiddleCenter); sub.gameObject.name="Subtitle"; UIFactory.Size(sub,44);
            var pass=UIFactory.Input("Password",panel,true); pass.gameObject.name="PasswordInput"; UIFactory.Size(pass,48);
            var status=UIFactory.Text("",panel,15,TextAnchor.MiddleCenter); status.gameObject.name="Status"; UIFactory.Size(status,34);
            var btn=UIFactory.Button("Login",panel,null); btn.gameObject.name="LoginButton"; UIFactory.Size(btn,50);
        }

        static void BuildStartup(RectTransform root)
        {
            var panel=UIFactory.Rect("StartupPanel",root,new Vector2(.18f,.1f),new Vector2(.82f,.9f),Vector2.zero,Vector2.zero);
            AddBackground(panel, new Color(.06f,.08f,.1f,.98f));
            UIFactory.VLayout(panel,10,28);
            var title=UIFactory.Text("SONAR Simulator Task",panel,34,TextAnchor.MiddleCenter); title.gameObject.name="Title"; UIFactory.Size(title,68);
            var version=UIFactory.Text("Version",panel,16,TextAnchor.MiddleCenter); version.gameObject.name="Version"; UIFactory.Size(version,28);
            var locRow=UIFactory.GO("LocationRow",panel); UIFactory.Size((RectTransform)locRow.transform,46); var locLayout=UIFactory.HLayout(locRow.transform,10,0); locLayout.childAlignment=TextAnchor.MiddleCenter; locLayout.childForceExpandWidth=false;
            var locLabel=UIFactory.Text("Location",locRow.transform,18,TextAnchor.MiddleRight); locLabel.gameObject.name="Label"; UIFactory.Size(locLabel,46,150);
            var loc=UIFactory.EditableCombo("",locRow.transform); loc.gameObject.name="LocationCombo"; UIFactory.Size(loc,46,-1,-1,1);

            var opRow=UIFactory.GO("OperatorRow",panel); UIFactory.Size((RectTransform)opRow.transform,46); var opLayout=UIFactory.HLayout(opRow.transform,10,0); opLayout.childAlignment=TextAnchor.MiddleCenter; opLayout.childForceExpandWidth=false;
            var opLabel=UIFactory.Text("Experimenter ID",opRow.transform,18,TextAnchor.MiddleRight); opLabel.gameObject.name="Label"; UIFactory.Size(opLabel,46,150);
            var op=UIFactory.EditableCombo("",opRow.transform); op.gameObject.name="OperatorCombo"; UIFactory.Size(op,46,-1,-1,1);

            var subjRow=UIFactory.GO("SubjectRow",panel); UIFactory.Size((RectTransform)subjRow.transform,46); var subjLayout=UIFactory.HLayout(subjRow.transform,10,0); subjLayout.childAlignment=TextAnchor.MiddleCenter; subjLayout.childForceExpandWidth=false;
            var subjLabel=UIFactory.Text("Subject ID",subjRow.transform,18,TextAnchor.MiddleRight); subjLabel.gameObject.name="Label"; UIFactory.Size(subjLabel,46,150);
            var subj=UIFactory.Input("",subjRow.transform); subj.gameObject.name="SubjectInput"; UIFactory.Size(subj,46,-1,-1,1);
            var status=UIFactory.Text("",panel,15,TextAnchor.MiddleCenter); status.gameObject.name="Status"; UIFactory.Size(status,30);

            // Flexible space keeps the footer at the bottom of the Startup panel.
            var verticalSpacer=UIFactory.GO("VerticalSpacer",panel); UIFactory.Size((RectTransform)verticalSpacer.transform,0,-1,1);

            // Keep Continue centered independently of platform-specific footer buttons.
            var continueRow=UIFactory.GO("ContinueRow",panel); UIFactory.Size((RectTransform)continueRow.transform,38);
            var continueLayout=UIFactory.HLayout(continueRow.transform,0,0); continueLayout.childAlignment=TextAnchor.MiddleCenter; continueLayout.childForceExpandHeight=false;
            var cont=UIFactory.Button("Continue",continueRow.transform,null); cont.gameObject.name="ContinueButton"; UIFactory.Size(cont,36,180);

            // Settings is Web-only; Exit Program is desktop-only. Both live in a separate footer.
            var footer=UIFactory.GO("Footer",panel); UIFactory.Size((RectTransform)footer.transform,38);
            var footerLayout=UIFactory.HLayout(footer.transform,10,0); footerLayout.childAlignment=TextAnchor.MiddleCenter; footerLayout.childForceExpandHeight=false; footerLayout.childForceExpandWidth=false;
            var settings=UIFactory.Button("Settings",footer.transform,null); settings.gameObject.name="SettingsButton"; UIFactory.Size(settings,36,160);
            var spacer=UIFactory.GO("FooterSpacer",footer.transform); UIFactory.Size((RectTransform)spacer.transform,36,-1,-1,1);
            var exit=UIFactory.Button("Exit Program",footer.transform,null); exit.gameObject.name="ExitProgramButton"; UIFactory.Size(exit,36,150);
        }

        static void BuildSelection(RectTransform root)
        {
            var main=UIFactory.Rect("Selection",root,new Vector2(.08f,.05f),new Vector2(.92f,.95f),Vector2.zero,Vector2.zero);
            AddBackground(main); UIFactory.VLayout(main,8,18);
            var head=UIFactory.Text("Location / Operator / Subject",main,18,TextAnchor.MiddleCenter); head.gameObject.name="Header"; UIFactory.Size(head,42);
            var title=UIFactory.Text("Select Experiment",main,27,TextAnchor.MiddleCenter); title.gameObject.name="Title"; UIFactory.Size(title,48);
            var scroll=UIFactory.GO("Scroll",main); UIFactory.Size((RectTransform)scroll.transform,300,-1,1);
            var sr=scroll.AddComponent<ScrollRect>();
            var viewport=UIFactory.Panel("Viewport",scroll.transform,new Color(.04f,.05f,.06f,1)); Fill(viewport.rectTransform); viewport.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var content=UIFactory.GO("Content",viewport.transform); var cr=(RectTransform)content.transform; cr.anchorMin=new Vector2(0,1);cr.anchorMax=new Vector2(1,1);cr.pivot=new Vector2(.5f,1);cr.sizeDelta=Vector2.zero;
            UIFactory.VLayout(content.transform,5,6); var fit=content.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;sr.viewport=viewport.rectTransform;sr.content=cr;sr.horizontal=false;
            var status=UIFactory.Text("",main,14,TextAnchor.MiddleCenter); status.gameObject.name="Status"; UIFactory.Size(status,30);
            var row=UIFactory.GO("Actions",main);UIFactory.Size((RectTransform)row.transform,38);var hl=UIFactory.HLayout(row.transform,8,0);hl.childAlignment=TextAnchor.MiddleCenter;hl.childForceExpandHeight=false;
            var back=UIFactory.Button("Back",row.transform,null);back.gameObject.name="BackButton";UIFactory.Size(back,36,140);
            var instructions=UIFactory.Button("Instructions",row.transform,null);instructions.gameObject.name="InstructionsButton";UIFactory.Size(instructions,36,160);
            var start=UIFactory.Button("Start",row.transform,null);start.gameObject.name="StartButton";UIFactory.Size(start,36,160);
        }

        static void BuildInstructions(RectTransform root)
        {
            var main=UIFactory.Rect("Instructions",root,new Vector2(.06f,.04f),new Vector2(.94f,.96f),Vector2.zero,Vector2.zero);AddBackground(main);UIFactory.VLayout(main,8,14);
            var title=UIFactory.Text("Instructions",main,27,TextAnchor.MiddleCenter);title.gameObject.name="Title";UIFactory.Size(title,50);
            var scroll=UIFactory.GO("Scroll",main);UIFactory.Size((RectTransform)scroll.transform,500,-1,1);var sr=scroll.AddComponent<ScrollRect>();
            var vp=UIFactory.Panel("Viewport",scroll.transform,new Color(.035f,.045f,.055f,1));Fill(vp.rectTransform);vp.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var content=UIFactory.GO("Content",vp.transform);var cr=(RectTransform)content.transform;cr.anchorMin=new Vector2(0,1);cr.anchorMax=new Vector2(1,1);cr.pivot=new Vector2(.5f,1);cr.sizeDelta=Vector2.zero;var fit=content.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;sr.viewport=vp.rectTransform;sr.content=cr;sr.horizontal=false;
            var back=UIFactory.Button("Back",main,null);back.gameObject.name="BackButton";UIFactory.Size(back,48);
        }

        static void BuildSettings(RectTransform root)
        {
            var main=UIFactory.Rect("Settings",root,new Vector2(.05f,.04f),new Vector2(.95f,.96f),Vector2.zero,Vector2.zero);AddBackground(main);UIFactory.VLayout(main,8,14);
            var title=UIFactory.Text("Web Operator Settings",main,28,TextAnchor.MiddleCenter);title.gameObject.name="Title";UIFactory.Size(title,50);
            var pass=UIFactory.Input("Re-enter operator password",main,true);pass.gameObject.name="PasswordInput";UIFactory.Size(pass,46);
            var status=UIFactory.Text("Re-authentication is required before settings are shown.",main,14,TextAnchor.MiddleCenter);status.gameObject.name="Status";UIFactory.Size(status,32);
            var unlock=UIFactory.Button("Unlock Settings",main,null);unlock.gameObject.name="UnlockButton";UIFactory.Size(unlock,46);
            var admin=UIFactory.GO("AdminBody",main);UIFactory.Size((RectTransform)admin.transform,550,-1,1);UIFactory.VLayout(admin.transform,8,8);admin.SetActive(false);
            var back=UIFactory.Button("Back",main,null);back.gameObject.name="BackButton";UIFactory.Size(back,46);
        }

        static void BuildSonarTask(RectTransform root)
        {
            var top=UIFactory.Rect("TopBar",root,new Vector2(0,.91f),new Vector2(1,1),Vector2.zero,Vector2.zero);AddBackground(top,new Color(.055f,.065f,.075f,1));var topLayout=UIFactory.HLayout(top,8,8);topLayout.childAlignment=TextAnchor.MiddleCenter;
            var sp=UIFactory.Button("Start",top,null);sp.gameObject.name="StartPauseButton";UIFactory.Size(sp,54,155);
            var phase=UIFactory.Text("Phase",top,23,TextAnchor.MiddleCenter);phase.gameObject.name="PhaseText";UIFactory.Size(phase,54,230);
            var timer=UIFactory.GO("ExperimentTimer",top).transform;UIFactory.Size((RectTransform)timer,54,255);var tv=UIFactory.VLayout(timer,0,0);tv.childAlignment=TextAnchor.MiddleRight;
            var elapsed=UIFactory.Text("",timer,17,TextAnchor.MiddleRight);elapsed.gameObject.name="ElapsedText";UIFactory.Size(elapsed,27);
            var remaining=UIFactory.Text("",timer,17,TextAnchor.MiddleRight);remaining.gameObject.name="RemainingText";UIFactory.Size(remaining,27);
            var info=UIFactory.Text("Subject / Experiment",top,16,TextAnchor.MiddleCenter);info.gameObject.name="InfoText";UIFactory.Size(info,54,-1,-1,1);
            var exit=UIFactory.Button("Exit",top,null);exit.gameObject.name="ExitButton";UIFactory.Size(exit,54,110);

            UIFactory.Rect("Left",root,new Vector2(0,0),new Vector2(.67f,.91f),new Vector2(8,8),new Vector2(-4,-4));
            var right=UIFactory.Rect("Right",root,new Vector2(.67f,0),new Vector2(1,.91f),new Vector2(4,8),new Vector2(-8,-4));AddBackground(right,new Color(.045f,.055f,.065f,1));
            var rc=UIFactory.Rect("RightContent",right,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);UIFactory.VLayout(rc,7,9);

            var train=UIFactory.Panel("Training",rc,new Color(.07f,.08f,.095f,1));UIFactory.Size(train,138);UIFactory.VLayout(train.transform,5,8);
            var th=UIFactory.Text("Training",train.transform,19,TextAnchor.MiddleCenter);th.gameObject.name="Title";UIFactory.Size(th,30);
            var td=UIFactory.PopupDropdown("Instructions",train.transform);td.gameObject.name="TrainingDropdown";UIFactory.Size(td,42);
            var ti=UIFactory.Text("Select a Signal to view it's default frequency positions in the LOFAR window(s)",train.transform,17,TextAnchor.MiddleCenter);ti.gameObject.name="TrainingInfo";UIFactory.Size(ti,55);

            var alert=UIFactory.Panel("Alerts",rc,new Color(.07f,.08f,.095f,1));UIFactory.Size(alert,145);UIFactory.VLayout(alert.transform,4,7);
            var ah=UIFactory.Text("Priority Search Alerts",alert.transform,18,TextAnchor.MiddleCenter);ah.gameObject.name="Title";UIFactory.Size(ah,30);
            var at=UIFactory.Text("\n\n\n\n\n",alert.transform,14,TextAnchor.UpperLeft);at.gameObject.name="AlertText";UIFactory.Size(at,100);

            var cls=UIFactory.Panel("Classification",rc,new Color(.07f,.08f,.095f,1));UIFactory.Size(cls,300,-1,1);UIFactory.VLayout(cls.transform,4,7);
            var ch=UIFactory.Text("Classification",cls.transform,19,TextAnchor.MiddleCenter);ch.gameObject.name="Title";UIFactory.Size(ch,30);
            var cg=UIFactory.GO("ClassGrid",cls.transform);var grid=cg.AddComponent<GridLayoutGroup>();grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;grid.cellSize=new Vector2(125,38);grid.spacing=new Vector2(5,5);UIFactory.Size((RectTransform)cg.transform,90);
            var confirm=UIFactory.Button("Confirm",cls.transform,null);confirm.gameObject.name="ConfirmButton";UIFactory.Size(confirm,38);
            var cl=UIFactory.Text("Confidence",cls.transform,15,TextAnchor.MiddleCenter);cl.gameObject.name="ConfidenceLabel";UIFactory.Size(cl,24);
            var cr=UIFactory.GO("ConfidenceRow",cls.transform);UIFactory.Size((RectTransform)cr.transform,44);var chl=UIFactory.HLayout(cr.transform,16,0);chl.childAlignment=TextAnchor.MiddleCenter;chl.childForceExpandHeight=false;chl.childForceExpandWidth=false;

            var feedback=UIFactory.Panel("Feedback",rc,new Color(.07f,.08f,.095f,1));UIFactory.Size(feedback,118);UIFactory.VLayout(feedback.transform,4,7);
            var fh=UIFactory.Text("Feedback",feedback.transform,18,TextAnchor.MiddleCenter);fh.gameObject.name="Title";UIFactory.Size(fh,28);
            var body=UIFactory.GO("FeedbackBody",feedback.transform);UIFactory.Size((RectTransform)body.transform,76);var fhl=UIFactory.HLayout(body.transform,8,0);fhl.childAlignment=TextAnchor.MiddleCenter;fhl.childForceExpandHeight=true;
            var box=UIFactory.Panel("FeedbackBox",body.transform,new Color(.045f,.055f,.065f,1));UIFactory.Size(box,70,190);var outline=box.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.75f,.78f,.82f,1);outline.effectDistance=new Vector2(1,-1);
            var ft=UIFactory.Text("",box.transform,18,TextAnchor.MiddleCenter);ft.gameObject.name="FeedbackText";
            var tally=UIFactory.Text("Correct: 0\nPartial: 0\nIncorrect: 0",body.transform,14,TextAnchor.MiddleLeft);tally.gameObject.name="TallyText";UIFactory.Size(tally,70,-1,-1,1);
        }

        static void AddBackground(RectTransform parent, Color? color = null)
        {
            var bg=UIFactory.Panel("BG",parent,color);Fill(bg.rectTransform);bg.transform.SetAsFirstSibling();
        }

        static void Fill(RectTransform r)
        {
            r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        }
    }
}
