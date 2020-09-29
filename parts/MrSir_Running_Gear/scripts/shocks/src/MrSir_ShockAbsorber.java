package java.game.parts.MrSir_Running_Gear.shocks;

import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.*;
import java.game.cars.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;

public class MrSir_ShockAbsorber extends ShockAbsorber{
	int parentType = 0;
	String sideFrontRear = "";
	public MrSir_ShockAbsorber( int id ){
		super( id );
		/*damping	= 500.0*0.75;// N/(m/s)
		max_damping	= 0.0;	// N/(m/s)
		min_damping	= 0.0;	// N/(m/s)
		max_length	= 0.45; //m
		min_length	= 0.15; //m
		rebound_factor	= 0.5;*/
		int	adjustable_damping		= 1;
		name = "Adjustable Shock absorber";
		prestige_calc_weight = 20.0;
		float	rebound_factor_mul 	= 0.75;
	}

	public void setParentType(int i){
		int renderType = getRenderType();
		int rootType = renderType-i;
		parentType = rootType;
	}
	
	public void calcStuffs(){
		if (whats_inside == WI_GAS){
			rebound_factor = 1.000*rebound_factor_mul;
		}else if (whats_inside == WI_OIL){
			rebound_factor = 0.750*rebound_factor_mul;
		}else if (whats_inside == WI_GAS_OIL){
			rebound_factor = 1.333*rebound_factor_mul;
		}
		
		value = brand_prestige_factor * max_damping / 25.0;
		brand_new_prestige_value = (value / 3.0)*2.0;

		//default_damping = damping;
		
		int dI=damping;
		name = name_prefix + " " + sideFrontRear + " " + dI+" N/m/s "+" adjustable shock";
	}

	public void load( File saveGame ){
		super.load( saveGame );
		int	save_ver = saveGame.readInt();
		if (save_ver >= 1){
			damping = saveGame.readFloat();
		}
	}

	public void save( File saveGame ){
		super.save( saveGame );
		int	save_ver = 2;
		saveGame.write( save_ver );
		if (save_ver >= 1){
			saveGame.write( damping );
		}
	}

	/*public void updatevariables()
	{
		WheelRef whl = getWheel();
		if (whl){
			whl.setDamping(damping*0.5, damping*rebound_factor*0.5);
		}
	}*/

	//---------tuning
	public int isTuneable(){
		return 1;
	}

	public void buildTuningMenu( Menu m ){
		old_damping = damping;
		m.addItem( "Bound damping",	1, damping, min_damping, max_damping, (max_damping-min_damping)/100+1, null ).printValue("   %1.0f N/m/s");
		m.addItem( "Reset to factory defaults",			0);	//this should always be with cmd=0
		GameLogic.player.car.wakeUp();
	}

	public void endTuningSession( int cancelled ){
		if( cancelled )	{
			damping = old_damping;
		}else{
			if (old_damping != damping)
				GameLogic.spendTime(5*60);
			getCar_LocalVersion();
			if (the_car)
				the_car.forceUpdate();
		}
		calcStuffs();
	}

	public void handleMessage( Event m ){
		if( m.cmd == 0 ){
			damping = default_damping;
			m.gadget.osd.findGadget( this, 1 ).setValue( damping );
		}else if( m.cmd == 1 ){
			damping = ((Slider)m.gadget).value;
		}
	}
	//---------tuning
}
