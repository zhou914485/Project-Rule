var go = GameObject.Find("Player");
var pc = go.GetComponent("PlayerController");
var drv = go.GetComponent("PlayerAnimationDriver");
var anim = go.GetComponentInChildren<Animator>();
var sr = go.GetComponentInChildren<SpriteRenderer>();
var cam = Camera.main;
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var facingF = pc.GetType().GetField("_facing", F);
var drvUpd = drv.GetType().GetMethod("Update", F);
facingF.SetValue(pc, -1f);            // 模拟「按左」后的朝向
anim.Play("Walk", 0, 0.25f);
for (int i = 0; i < 6; i++) anim.Update(1f / 60f);
drvUpd.Invoke(drv, null);             // 让 driver 真的写一次 flipX
var p = go.transform.position;
cam.transform.position = new Vector3(p.x, p.y + 0.35f, -10f);
cam.orthographicSize = 1.1f;
int W = 700, Hh = 620;
var rt = new RenderTexture(W, Hh, 24, RenderTextureFormat.ARGB32);
var pt = cam.targetTexture; var pa = RenderTexture.active;
cam.targetTexture = rt; cam.Render();
RenderTexture.active = rt;
var tex = new Texture2D(W, Hh, TextureFormat.RGB24, false);
tex.ReadPixels(new Rect(0, 0, W, Hh), 0, 0); tex.Apply();
RenderTexture.active = pa; cam.targetTexture = pt;
System.IO.File.WriteAllBytes("D:/Users/lenovo/Documents/GitHub/Project-Rule/Project-Rule/ArtSource/_look_left.png", tex.EncodeToPNG());
return "Facing=-1 时 driver 设了 flipX=" + sr.flipX + "  clip=" + anim.GetCurrentAnimatorClipInfo(0)[0].clip.name;
