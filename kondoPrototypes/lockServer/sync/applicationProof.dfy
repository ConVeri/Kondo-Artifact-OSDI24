include "spec.dfy"

module LockServerProof {
  
import opened Types
import opened UtilitiesLibrary
import opened System
import opened Obligations

ghost predicate ServerOwnsLockImpliesNoClientsOwnsLock(c: Constants, v: Variables)
  requires v.WF(c)
{
  v.server[0].hasLock ==> 
  (forall id | c.ValidClientIdx(id) :: !v.clients[id].hasLock)
}

// Protocol bundle: 1 clause in total
ghost predicate ProtocolInv(c: Constants, v: Variables)
  requires v.WF(c)
{
  && ServerOwnsLockImpliesNoClientsOwnsLock(c, v)
}

ghost predicate Inv(c: Constants, v: Variables)
{
  && v.WF(c)
  && ProtocolInv(c, v)
  && Safety(c, v)
}


/***************************************************************************************
*                                Top-level Obligations                                 *
***************************************************************************************/


lemma InvImpliesSafety(c: Constants, v: Variables)
  requires Inv(c, v)
  ensures Safety(c, v)
{}

lemma InitImpliesInv(c: Constants, v: Variables)
  requires Init(c, v)
  ensures Inv(c, v)
{}

lemma InvInductive(c: Constants, v: Variables, v': Variables)
  requires Inv(c, v)
  requires Next(c, v, v')
  ensures Inv(c, v')
{
  InvNextServerOwnsLockImpliesNoClientsOwnsLock(c, v, v');
}

/***************************************************************************************
*                                 InvNext Proofs                                       *
***************************************************************************************/

lemma InvNextServerOwnsLockImpliesNoClientsOwnsLock(c: Constants, v: Variables, v': Variables)
  requires Inv(c, v)
  requires Next(c, v, v')
  ensures ServerOwnsLockImpliesNoClientsOwnsLock(c, v') 
{}

} // end module LockServerProof