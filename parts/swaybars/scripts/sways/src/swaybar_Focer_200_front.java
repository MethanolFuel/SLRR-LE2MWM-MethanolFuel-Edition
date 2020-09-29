package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Focer_200_front extends Swaybar
{
	public swaybar_Focer_200_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Shimutshibu Focer 200 front";
		brand_prestige_factor = 1.28;
		calcStuffs();
	}
}

