package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Einvagen_GTK_rear extends Swaybar
{
	public swaybar_Einvagen_GTK_rear( int id )
	{
		super( id );

		force = 8500.0;
		damping = 850.0;

		name_prefix = "Einvagen 110 GTK rear";
		brand_prestige_factor = 1.03;
		calcStuffs();
	}
}
