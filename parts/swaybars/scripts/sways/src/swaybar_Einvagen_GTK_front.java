package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Einvagen_GTK_front extends Swaybar
{
	public swaybar_Einvagen_GTK_front( int id )
	{
		super( id );

		force = 10500.0;
		damping = 1050.0;

		name_prefix = "Einvagen 110 GTK front";
		brand_prestige_factor = 1.03;
		calcStuffs();
	}
}
