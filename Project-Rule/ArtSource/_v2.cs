var sb = new System.Text.StringBuilder();
string p = "Assets/_Project/Art/Sprites/Hero/Idle/Hero_Idle_01.png";
var imp = AssetImporter.GetAtPath(p) as TextureImporter;
sb.Append("Idle meta: ppu=").Append(imp.spritePixelsPerUnit)
  .Append(" compress=").Append(imp.textureCompression)
  .Append(" mip=").Append(imp.mipmapEnabled)
  .Append(" filter=").Append(imp.filterMode).AppendLine();
// 角色世界高度（用 alpha 高的那个序列 = Idle）
var sp = AssetDatabase.LoadAssetAtPath<Sprite>(p);
sb.Append("Idle 世界尺寸: ").Append(sp.bounds.size.x.ToString("F3")).Append(" x ").Append(sp.bounds.size.y.ToString("F3"));
var go = GameObject.Find("Player");
if (go != null && go.GetComponentInChildren<SpriteRenderer>() != null)
{
    var sr = go.GetComponentInChildren<SpriteRenderer>();
    sb.AppendLine().Append("Player 当前 sprite=").Append(sr.sprite == null ? "null" : sr.sprite.name)
      .Append(" world=").Append(sr.bounds.size.x.ToString("F3")).Append("x").Append(sr.bounds.size.y.ToString("F3"));
}
return sb.ToString();
