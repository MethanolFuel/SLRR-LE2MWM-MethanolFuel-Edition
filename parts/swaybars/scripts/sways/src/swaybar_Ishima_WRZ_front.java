package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Ishima_WRZ_front extends Swaybar
{
	public swaybar_Ishima_WRZ_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Ishima Enula WRZ front";
		brand_prestige_factor = 1.50;
		calcStuffs();
	}
}

