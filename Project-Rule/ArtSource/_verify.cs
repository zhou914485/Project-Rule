var sb = new System.Text.StringBuilder();
foreach (var seq in new string[]{"Idle","Walk","Run","Airborne","WallJump","Weightless","Slash1"})
{
    string p = "Assets/_Project/Art/Sprites/Hero/" + seq + "/Hero_" + seq + "_01.png";
    var tex = AssetDatabase.LoadAssetAtPath<Texture>(p);
    var imp = AssetImporter.GetAtPath(p) as TextureImporter;
    var spi = AssetDatabase.LoadAssetAtPath<Sprite>(p);
    sb.Append(seq).Append(": tex=").Append(tex == null ? "null" : tex.width + "x" + tex.height)
      .Append(" ppu=").Append(imp.spritePixelsPerUnit)
      .Append(" pivot=").Append(spi == null ? "?" : spi.pivot.ToString())
      .AppendLine();
}
var go = GameObject.Find("Player");
if (go != null)
{
    var sr = go.GetComponentInChildren<SpriteRenderer>();
    if (sr != null && sr.sprite != null)
        sb.Append("Player 实机 world=").Append(sr.bounds.size.x.ToString("F3")).Append("x").Append(sr.bounds.size.y.ToString("F3"));
}
return sb.ToString();
